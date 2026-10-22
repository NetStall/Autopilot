using System.IO.Compression;
using JobAutopilot.Api.Features.Documents;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Documents;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Packages;

/// <summary>The e-mail part of a package: everything to copy into my own mail program. Nothing is sent from here.</summary>
public sealed record EmailPartDto(string? Recipient, string Subject, string Salutation, string Body, string Closing, string SenderName, IReadOnlyList<string> Attachments);

/// <summary>The portal / form part: what to fill in, short texts to paste, the posting.</summary>
public sealed record PortalPartDto(IReadOnlyList<ChecklistItem> Checklist, string Motivation, string AboutMe, string? PostingUrl);

/// <summary>„Bewerbungspaket" (08c) of one application.</summary>
public sealed record PackageDto(
    Guid ApplicationId, Guid? VacancyId, string Company, string Position, ApplicationStatus Status, string StatusLabel, bool Approved,
    HowToApply HowToApply, EmailPartDto Email, PortalPartDto Portal, IReadOnlyList<DraftWarning> Warnings, IReadOnlyList<DraftWarning> LetterWarnings,
    DateTimeOffset GeneratedAt, int Version);

/// <summary>One row of „Heute bereit".</summary>
public sealed record ReadyPackageDto(
    Guid ApplicationId, Guid? VacancyId, string Company, string Position, ApplicationStatus Status, ApplyChannel Channel, string? Email,
    bool Approved, int Warnings, DateTimeOffset GeneratedAt);

public sealed record PackageCandidateDto(Guid VacancyId, string Title, string Company, int? RankScore);

/// <summary>Before „Top K vorbereiten": which vacancies, how many calls, what it costs, whether today's limit allows it.</summary>
public sealed record PackageEstimateDto(int K, IReadOnlyList<PackageCandidateDto> Candidates, int Calls, decimal CostUsd, int RemainingCalls, bool WithinLimit);

public sealed record PackageResultDto(Guid VacancyId, string Title, Guid? ApplicationId, string? Error);

public static class PackageEndpoints
{
    /// <summary>Letter + package texts; one more if the letter is generated again automatically (06c / 08a).</summary>
    public const int CallsPerPackage = 2;

    public static IEndpointRouteBuilder MapPackageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/vacancies/{id:guid}/package", CreateForVacancy).WithTags("Packages").WithName("CreatePackage");
        var packages = app.MapGroup("/api/packages").WithTags("Packages");
        packages.MapGet("/", Ready).WithName("ReadyPackages");
        packages.MapGet("/estimate", Estimate).WithName("EstimatePackages");
        packages.MapPost("/top", PrepareTop).WithName("PrepareTopPackages");
        app.MapGet("/api/applications/{id:guid}/package", Get).WithTags("Packages").WithName("GetPackage");
        app.MapGet("/api/applications/{id:guid}/package.zip", Zip).WithTags("Packages").WithName("DownloadPackage");
        return app;
    }

    /// <summary>„Bewerbungspaket erstellen": documents (if not there yet) and the channel texts.</summary>
    private static Task<IResult> CreateForVacancy(
        Guid id, ApplicationDocumentsService documents, AppDbContext db, ISearchProfileSource searchProfiles, CancellationToken ct) =>
        DocumentEndpoints.Run(async () =>
        {
            var applicationId = await PrepareAsync(id, documents, ct);
            return TypedResults.Ok(await BuildAsync(applicationId, documents, db, searchProfiles, ct));
        });

    private static async Task<Guid> PrepareAsync(Guid vacancyId, ApplicationDocumentsService documents, CancellationToken ct)
    {
        var (view, _) = await documents.CreateForVacancyAsync(vacancyId, ct);
        if (view.Draft.Package is null)
        {
            await documents.CreatePackageAsync(view.Application.Id, view.Draft.Version, ct);
        }

        return view.Application.Id;
    }

    private static Task<IResult> Get(Guid id, ApplicationDocumentsService documents, AppDbContext db, ISearchProfileSource searchProfiles, CancellationToken ct) =>
        DocumentEndpoints.Run(async () => TypedResults.Ok(await BuildAsync(id, documents, db, searchProfiles, ct)));

    /// <summary>„Heute bereit": prepared packages not sent yet, oldest first.</summary>
    private static async Task<Ok<List<ReadyPackageDto>>> Ready(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.ApplicationDrafts.AsNoTracking()
            .Where(d => d.Package != null)
            .Join(db.Applications.AsNoTracking().Where(a => a.Status == ApplicationStatus.Draft || a.Status == ApplicationStatus.Approved),
                d => d.ApplicationId, a => a.Id, (d, a) => new { Draft = d, Application = a })
            .ToListAsync(ct);
        var vacancyIds = rows.Select(r => r.Application.VacancyId).OfType<Guid>().ToList();
        var vacancies = await db.Vacancies.AsNoTracking().Where(v => vacancyIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Description, v.Url, v.ExtractionJson }).ToDictionaryAsync(v => v.Id, ct);
        return TypedResults.Ok(rows
            .Select(r =>
            {
                var v = r.Application.VacancyId is { } vid ? vacancies.GetValueOrDefault(vid) : null;
                var how = HowToApplyDetector.Resolve(v?.Description, v?.Url, StoredExtraction.ForDescription(v?.ExtractionJson, v?.Description)?.Result.Application);
                return new ReadyPackageDto(r.Application.Id, r.Application.VacancyId, r.Application.Company, r.Application.Position, r.Application.Status,
                    how.Channel, how.Email, r.Draft.ApprovedAt != null, r.Draft.Package!.Warnings.Count + r.Draft.Warnings.Count(w => w.Severity == WarningSeverity.Warning),
                    r.Draft.Package.GeneratedAt);
            })
            .OrderBy(r => r.GeneratedAt)
            .ToList());
    }

    private static async Task<Ok<PackageEstimateDto>> Estimate(
        AppDbContext db, ApplicationDocumentsService documents, ISearchProfileSource searchProfiles, TimeProvider timeProvider, CancellationToken ct, int k = 5)
    {
        k = Math.Clamp(k, 1, 20);
        var candidates = await CandidatesAsync(db, searchProfiles, timeProvider, k, ct);
        var (letterIn, letterOut) = await documents.AverageTokensAsync(DraftRevisionKind.Generated, ct);
        var (packageIn, packageOut) = await documents.AverageTokensAsync(DraftRevisionKind.Package, ct);
        var cost = candidates.Count * (documents.EstimateCostUsd(letterIn, letterOut) + documents.EstimateCostUsd(packageIn, packageOut));
        var remaining = await documents.RemainingCallsAsync(ct);
        var calls = candidates.Count * CallsPerPackage;
        return TypedResults.Ok(new PackageEstimateDto(k, candidates, calls, Math.Round(cost, 3), remaining, calls <= remaining));
    }

    /// <summary>„Top K vorbereiten": one package after the other, until K are done or today's call limit would be passed.</summary>
    private static async Task<Ok<List<PackageResultDto>>> PrepareTop(
        AppDbContext db, ApplicationDocumentsService documents, ISearchProfileSource searchProfiles, TimeProvider timeProvider, CancellationToken ct, int k = 5)
    {
        var results = new List<PackageResultDto>();
        foreach (var candidate in await CandidatesAsync(db, searchProfiles, timeProvider, Math.Clamp(k, 1, 20), ct))
        {
            if (await documents.RemainingCallsAsync(ct) < CallsPerPackage)
            {
                results.Add(new PackageResultDto(candidate.VacancyId, candidate.Title, null, "Tageslimit erreicht (Claude:DailyCallLimit)."));
                continue;
            }

            try
            {
                results.Add(new PackageResultDto(candidate.VacancyId, candidate.Title, await PrepareAsync(candidate.VacancyId, documents, ct), null));
            }
            catch (DocumentException ex)
            {
                results.Add(new PackageResultDto(candidate.VacancyId, candidate.Title, null, ex.Message));
            }
        }

        return TypedResults.Ok(results);
    }

    /// <summary>Attachments as one ZIP — only after „Freigeben", when the PDFs exist (that is when I may send).</summary>
    private static async Task<IResult> Zip(Guid id, ApplicationDocumentsService documents, CancellationToken ct)
    {
        var letter = await documents.GetPdfAsync(id, letter: true, ct);
        var cv = await documents.GetPdfAsync(id, letter: false, ct);
        if (letter is null || cv is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Noch nicht freigegeben.", detail: "Die Anhänge entstehen beim Freigeben.");
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in new[] { letter.Value, cv.Value })
            {
                await using var entry = await zip.CreateEntry(name, CompressionLevel.Optimal).OpenAsync(ct);
                await entry.WriteAsync(content, ct);
            }
        }

        return TypedResults.File(buffer.ToArray(), "application/zip", Path.GetFileNameWithoutExtension(letter.Value.FileName).Replace("_Anschreiben", "_Bewerbung", StringComparison.Ordinal) + ".zip");
    }

    /// <summary>The visible list by rank: without an application (also not for a duplicate), not filtered, with a description.</summary>
    private static async Task<List<PackageCandidateDto>> CandidatesAsync(AppDbContext db, ISearchProfileSource searchProfiles, TimeProvider timeProvider, int k, CancellationToken ct)
    {
        var profile = searchProfiles.Current.Profile;
        var filter = new VacancyFilter(NotExpiredAt: timeProvider.GetUtcNow(), HiddenEmployerTypes: profile.HiddenEmployerTypes(), HideFiltered: true);
        return await VacancyEndpoints.Filtered(db, filter)
            .Where(v => v.Description != null && v.Description != string.Empty)
            .Where(v => !db.Applications.Any(a => a.VacancyId == v.Id || (a.DuplicateKey != null && a.DuplicateKey == v.DuplicateKey)))
            .ApplySort(VacancySort.Relevance).Take(k)
            .Select(v => new PackageCandidateDto(v.Id, v.Title, v.Company, v.RankScore))
            .ToListAsync(ct);
    }

    private static async Task<PackageDto> BuildAsync(Guid applicationId, ApplicationDocumentsService documents, AppDbContext db, ISearchProfileSource searchProfiles, CancellationToken ct)
    {
        var view = await documents.GetAsync(applicationId, ct);
        var draft = view.Draft;
        var package = draft.Package ?? throw new DocumentException(DocumentProblem.NotFound, "Für diese Bewerbung gibt es noch kein Bewerbungspaket.");
        var vacancy = view.Application.VacancyId is { } vid ? await db.Vacancies.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vid, ct) : null;
        var how = HowToApplyDetector.Resolve(vacancy?.Description, vacancy?.Url,
            StoredExtraction.ForDescription(vacancy?.ExtractionJson, vacancy?.Description)?.Result.Application);
        var letter = draft.Letter;
        var sender = view.Cv.Name;
        var attachments = new[]
        {
            DocumentFileStore.FileName(sender, letter.Language, letter: true, view.Application.Company),
            DocumentFileStore.FileName(sender, letter.Language, letter: false, view.Application.Company),
        };
        var salutation = how.Salutation is { } named && letter.Formality == Formality.Sie ? named : letter.Salutation;
        var email = new EmailPartDto(how.Email, letter.Subject, salutation, package.EmailBody, letter.Closing, sender, attachments);
        var portal = new PortalPartDto(ApplicationPackage.Checklist(how, searchProfiles.Current.Profile, letter.Language), package.Motivation, package.AboutMe,
            vacancy?.Url);
        return new PackageDto(view.Application.Id, view.Application.VacancyId, view.Application.Company, view.Application.Position, view.Application.Status,
            ApplicationStatuses.Label(view.Application.Status), draft.ApprovedAt is not null, how, email, portal, package.Warnings, draft.Warnings,
            package.GeneratedAt, draft.Version);
    }
}
