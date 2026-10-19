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
        group.MapPost("/seen", MarkSeen).WithName(nameof(MarkSeen));
        group.MapPost("/rescore", StartRescore)
            .WithName(nameof(StartRescore))
            .RequireRateLimiting(ApiSecurity.ImportRateLimitPolicy);
        group.MapGet("/rescore", GetRescoreStatus).WithName(nameof(GetRescoreStatus));

        return app;
    }

    private static async Task<Ok<PagedResponse<VacancyDto>>> ListVacancies(
        [AsParameters] VacancyListQuery request,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var filter = request.ToFilter() with { NotExpiredAt = timeProvider.GetUtcNow() };
        var page = request.PageOrDefault;
        var pageSize = request.PageSizeOrDefault;

        var pool = filter.Source is { } source ? db.Vacancies.Where(v => v.Source == source) : db.Vacancies;
        var query = db.Vacancies.AsNoTracking().ApplyFilter(filter).CollapseDuplicates(pool);

        var totalCount = await query.CountAsync(cancellationToken);
        var vacancies = await query
            .ApplySort(filter.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // The list does not need the (long) descriptions; the detail endpoint returns them.
        var duplicates = await DuplicatesOfAsync(db, vacancies, cancellationToken);
        var items = vacancies.ConvertAll(v => v.ToDto(includeDescription: false, AlsoFoundAt(v, duplicates)));
        return TypedResults.Ok(new PagedResponse<VacancyDto>(items, page, pageSize, totalCount));
    }

    private static async Task<Results<Ok<VacancyDto>, NotFound>> GetVacancy(
        Guid id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (vacancy is null)
        {
            return TypedResults.NotFound();
        }

        var duplicates = await DuplicatesOfAsync(db, [vacancy], cancellationToken);
        return TypedResults.Ok(vacancy.ToDto(alsoFoundAt: AlsoFoundAt(vacancy, duplicates)));
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
            result.Penalty));
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

    private static async Task<CreatedAtRoute<VacancyDto>> CreateVacancy(
        CreateVacancyRequest request,
        AppDbContext db,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var vacancy = request.ToManualVacancy(timeProvider.GetUtcNow());

        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync(cancellationToken);

        var logger = loggerFactory.CreateLogger(LogCategory);
        LogManualVacancyCreated(logger, vacancy.Id);
        return TypedResults.CreatedAtRoute(vacancy.ToDto(), nameof(GetVacancy), new { id = vacancy.Id });
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
