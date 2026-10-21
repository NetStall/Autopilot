using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Scoring;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Vacancies;

public static partial class VacancyEndpoints
{
    private const string LogCategory = "JobAutopilot.Api.Vacancies";

    public static IEndpointRouteBuilder MapVacancyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vacancies").WithTags("Vacancies");

        group.MapGet("/", ListVacancies).WithName(nameof(ListVacancies));
        group.MapGet("/{id:guid}", GetVacancy).WithName(nameof(GetVacancy));
        group.MapGet("/{id:guid}/fit", GetFit).WithName(nameof(GetFit));
        group.MapPost("/", CreateVacancy).WithName(nameof(CreateVacancy));
        group.MapPatch("/{id:guid}", UpdateVacancy).WithName(nameof(UpdateVacancy));
        group.MapPut("/{id:guid}/description", SetDescription).WithName(nameof(SetDescription));
        group.MapPost("/seen", MarkSeen).WithName(nameof(MarkSeen));
        group.MapPost("/rescore", StartRescore)
            .WithName(nameof(StartRescore))
            .RequireRateLimiting(ApiSecurity.ImportRateLimitPolicy);
        group.MapGet("/rescore", GetRescoreStatus).WithName(nameof(GetRescoreStatus));

        return app;
    }

    /// <summary>
    /// The list with paging, the „Top N" cut (after all other filters, by rank) and facet counts: for every facet the
    /// counts of its values under all other active filters (so the numbers next to „Quelle" do not depend on the
    /// selected sources).
    /// </summary>
    private static async Task<Ok<VacancyPageDto>> ListVacancies(
        [AsParameters] VacancyListQuery request,
        AppDbContext db,
        TimeProvider timeProvider,
        Core.SearchProfiles.ISearchProfileSource searchProfiles,
        CancellationToken cancellationToken)
    {
        var filter = request.ToFilter() with
        {
            NotExpiredAt = timeProvider.GetUtcNow(),
            HiddenEmployerTypes = searchProfiles.Current.Profile.HiddenEmployerTypes(),
            HideUnreachable = searchProfiles.Current.Profile.HideUnreachable && request.IncludeUnreachable != true,
        };
        var page = request.PageOrDefault;
        var pageSize = request.PageSizeOrDefault;

        var query = Filtered(db, filter);
        var matching = await query.CountAsync(cancellationToken);
        var sorted = query.ApplySort(filter.Sort);
        var totalCount = request.Top is { } top ? Math.Min(top, matching) : matching;
        IQueryable<Core.Vacancies.Vacancy> window = request.Top is { } n ? sorted.Take(n) : sorted;
        var vacancies = await window
            .Skip((page - 1) * pageSize)
            .Take(Math.Max(0, Math.Min(pageSize, totalCount - ((page - 1) * pageSize))))
            .ToListAsync(cancellationToken);
        var facets = await FacetsAsync(db, filter, cancellationToken);

        // The list does not need the (long) descriptions; the detail endpoint returns them.
        var duplicates = await DuplicatesOfAsync(db, vacancies, cancellationToken);
        var applications = await db.Applications.AsNoTracking().ToListAsync(cancellationToken);
        var items = vacancies.ConvertAll(v =>
            v.ToDto(includeDescription: false, AlsoFoundAt(v, duplicates), AppliedTo(v, duplicates, applications)));
        return TypedResults.Ok(new VacancyPageDto(items, page, pageSize, totalCount, matching, request.Top, facets));
    }

    internal static IQueryable<Core.Vacancies.Vacancy> Filtered(AppDbContext db, Core.Vacancies.VacancyFilter filter)
    {
        var pool = filter.Sources is { Count: > 0 } sources ? db.Vacancies.Where(v => sources.Contains(v.Source)) : db.Vacancies;
        return db.Vacancies.AsNoTracking().ApplyFilter(filter).CollapseDuplicates(pool);
    }

    private static async Task<VacancyFacetsDto> FacetsAsync(AppDbContext db, Core.Vacancies.VacancyFilter filter, CancellationToken ct)
    {
        var sources = await Filtered(db, filter with { Sources = null })
            .GroupBy(v => v.Source).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var remote = await Filtered(db, filter with { RemoteLevels = null })
            .GroupBy(v => v.RemoteLevel).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var employers = await Filtered(db, filter with { EmployerTypes = null, HiddenEmployerTypes = null })
            .GroupBy(v => v.EmployerType).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var relevance = await Filtered(db, filter with { RelevanceLevels = null })
            .GroupBy(v => v.RelevanceLevel).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var german = await Filtered(db, filter with { RequiredGerman = null })
            .GroupBy(v => v.RequiredGerman).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return new VacancyFacetsDto(
            sources.ToDictionary(x => x.Key.ToString(), x => x.Count),
            remote.ToDictionary(x => x.Key.ToString(), x => x.Count),
            employers.ToDictionary(x => x.Key.ToString(), x => x.Count),
            // Not scored yet counts as „Maybe", like the filter.
            relevance.GroupBy(x => (x.Key ?? Core.Vacancies.RelevanceLevel.Maybe).ToString())
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Count)),
            german.ToDictionary(x => x.Key.ToString(), x => x.Count));
    }

    private static async Task<Results<Ok<VacancyDto>, NotFound>> GetVacancy(
        Guid id,
        AppDbContext db,
        Core.Profile.ICandidateProfileSource profiles,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vacancy is null)
        {
            return TypedResults.NotFound();
        }

        var duplicates = await DuplicatesOfAsync(db, [vacancy], cancellationToken);
        var applications = await db.Applications.AsNoTracking().ToListAsync(cancellationToken);
        var label = await db.GoldLabels.AsNoTracking().Where(l => l.VacancyId == id).Select(l => (bool?)l.Fits).FirstOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(vacancy.ToDto(
            alsoFoundAt: AlsoFoundAt(vacancy, duplicates), applied: AppliedTo(vacancy, duplicates, applications), goldLabel: label,
            profile: profiles.Current.Usable));
    }

    /// <summary>
    /// The application for this vacancy, one of its duplicates, or the same company with a similar title — newest
    /// first. Few applications exist, so this is matched in memory.
    /// </summary>
    private static AppliedDto? AppliedTo(
        Core.Vacancies.Vacancy vacancy,
        ILookup<string, Core.Vacancies.Vacancy> duplicates,
        IReadOnlyList<Core.Applications.Application> applications)
    {
        var others = vacancy.DuplicateKey is null ? [] : duplicates[vacancy.DuplicateKey].ToList();
        var match = applications
            .Where(a => Core.Applications.ApplicationMatcher.Matches(a, vacancy) || others.Any(o => a.VacancyId == o.Id))
            .OrderByDescending(a => a.AppliedAt)
            .FirstOrDefault();
        return match is null
            ? null
            : new AppliedDto(match.Id, match.Status, Core.Applications.ApplicationStatuses.Label(match.Status), match.AppliedAt);
    }

    /// <summary>„Passt zu mir?" — the fit check of one vacancy against the current profile (computed, not stored).</summary>
    private static async Task<Results<Ok<FitDto>, NotFound>> GetFit(
        Guid id,
        AppDbContext db,
        Core.Fit.VacancyFitService fit,
        Core.Profile.ICandidateProfileSource profiles,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vacancy is null)
        {
            return TypedResults.NotFound();
        }

        var result = fit.Analyze(vacancy);
        return TypedResults.Ok(new FitDto(
            profiles.Current.Status,
            vacancy.Description is not null,
            result.Requirements,
            result.Warnings,
            result.RequiredGerman,
            result.RequiredYears,
            result.Penalty,
            result.Source));
    }

    /// <summary>All vacancies sharing a duplicate key with one of <paramref name="vacancies"/>, by key.</summary>
    private static async Task<ILookup<string, Core.Vacancies.Vacancy>> DuplicatesOfAsync(
        AppDbContext db,
        IReadOnlyCollection<Core.Vacancies.Vacancy> vacancies,
        CancellationToken cancellationToken)
    {
        var keys = vacancies.Select(v => v.DuplicateKey).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
        {
            return Array.Empty<Core.Vacancies.Vacancy>().ToLookup(v => v.DuplicateKey!);
        }

        var found = await db.Vacancies.AsNoTracking()
            .Where(v => v.DuplicateKey != null && keys.Contains(v.DuplicateKey))
            .OrderBy(v => v.FetchedAt)
            .ToListAsync(cancellationToken);
        return found.ToLookup(v => v.DuplicateKey!, StringComparer.Ordinal);
    }

    private static List<VacancyLinkDto> AlsoFoundAt(Core.Vacancies.Vacancy vacancy, ILookup<string, Core.Vacancies.Vacancy> duplicates) =>
        vacancy.DuplicateKey is null
            ? []
            : [.. duplicates[vacancy.DuplicateKey].Where(d => d.Id != vacancy.Id).Select(d => new VacancyLinkDto(d.Id, d.Source, d.Url))];

    /// <summary>
    /// „Stelle manuell hinzufügen" (LinkedIn, company sites): stored with source „Manuell", the pasted text marked as
    /// manual, and scored right away like any imported vacancy — the link is never fetched.
    /// </summary>
    private static async Task<CreatedAtRoute<VacancyDto>> CreateVacancy(
        CreateVacancyRequest request,
        AppDbContext db,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        Core.Scoring.VacancyEnricher enricher,
        CancellationToken cancellationToken)
    {
        var vacancy = request.ToManualVacancy(timeProvider.GetUtcNow());
        if (!string.IsNullOrWhiteSpace(vacancy.Description))
        {
            vacancy.Description = vacancy.Description.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            vacancy.DescriptionIsManual = true;
            vacancy.DetailsFetchedAt = vacancy.FetchedAt;
        }

        enricher.Apply(vacancy);

        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync(cancellationToken);

        var logger = loggerFactory.CreateLogger(LogCategory);
        LogManualVacancyCreated(logger, vacancy.Id);
        return TypedResults.CreatedAtRoute(vacancy.ToDto(), nameof(GetVacancy), new { id = vacancy.Id });
    }

    /// <summary>
    /// „Beschreibung einfügen": the text pasted from the original posting (plain text, no scraping). It is marked as
    /// manual, so fetching details never replaces it, and the vacancy is scored again right away.
    /// </summary>
    private static async Task<Results<Ok<VacancyDto>, NotFound, ValidationProblem>> SetDescription(
        Guid id,
        SetDescriptionRequest request,
        AppDbContext db,
        Core.Scoring.VacancyEnricher enricher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var text = request.Description?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(text) || text.Length > Core.Vacancies.VacancyLimits.DescriptionMaxLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["description"] = [$"Die Beschreibung darf nicht leer sein und höchstens {Core.Vacancies.VacancyLimits.DescriptionMaxLength} Zeichen haben."],
            });
        }

        var vacancy = await db.Vacancies.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vacancy is null)
        {
            return TypedResults.NotFound();
        }

        vacancy.Description = text;
        vacancy.DescriptionIsManual = true;
        vacancy.DetailsFetchedAt ??= timeProvider.GetUtcNow();
        enricher.Apply(vacancy);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(vacancy.ToDto());
    }

    /// <summary>Triage state only: <c>isSeen</c> and/or <c>interest</c>.</summary>
    private static async Task<Results<NoContent, NotFound>> UpdateVacancy(
        Guid id,
        UpdateVacancyRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vacancy is null)
        {
            return TypedResults.NotFound();
        }

        if (request.IsSeen is { } isSeen)
        {
            vacancy.IsSeen = isSeen;
        }

        if (request.ParsedInterest is { } interest)
        {
            vacancy.Interest = interest;
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Queues re-scoring of all vacancies with the current configuration → 202 with the status.</summary>
    private static Accepted<RescoreStatus> StartRescore(RescoreCoordinator coordinator) =>
        TypedResults.Accepted("/api/vacancies/rescore", coordinator.Request());

    private static Ok<RescoreStatus> GetRescoreStatus(RescoreCoordinator coordinator) =>
        TypedResults.Ok(coordinator.Status);

    /// <summary>Marks up to <see cref="MarkSeenRequest.MaxIds"/> vacancies as seen in one statement.</summary>
    private static async Task<NoContent> MarkSeen(
        MarkSeenRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var ids = request.Ids!.Distinct().ToList();
        await db.Vacancies
            .Where(v => ids.Contains(v.Id) && !v.IsSeen)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsSeen, true), cancellationToken);

        return TypedResults.NoContent();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created manual vacancy {VacancyId}")]
    private static partial void LogManualVacancyCreated(ILogger logger, Guid vacancyId);
}

/// <summary>Body of <c>PUT /api/vacancies/{id}/description</c>.</summary>
public sealed record SetDescriptionRequest(string? Description);
