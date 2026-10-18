using JobAutopilot.Api.Common;
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
        group.MapPost("/", CreateVacancy).WithName(nameof(CreateVacancy));
        group.MapPatch("/{id:guid}", UpdateVacancy).WithName(nameof(UpdateVacancy));
        group.MapPost("/seen", MarkSeen).WithName(nameof(MarkSeen));

        return app;
    }

    private static async Task<Ok<PagedResponse<VacancyDto>>> ListVacancies(
        [AsParameters] VacancyListQuery request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var filter = request.ToFilter();
        var page = request.PageOrDefault;
        var pageSize = request.PageSizeOrDefault;

        var query = db.Vacancies.AsNoTracking().ApplyFilter(filter);

        var totalCount = await query.CountAsync(cancellationToken);
        var vacancies = await query
            .ApplySort(filter.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = vacancies.ConvertAll(v => v.ToDto());
        return TypedResults.Ok(new PagedResponse<VacancyDto>(items, page, pageSize, totalCount));
    }

    private static async Task<Results<Ok<VacancyDto>, NotFound>> GetVacancy(
        Guid id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var vacancy = await db.Vacancies.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        return vacancy is null ? TypedResults.NotFound() : TypedResults.Ok(vacancy.ToDto());
    }

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

    /// <summary>Only <c>isSeen</c> is patchable in this step.</summary>
    private static async Task<Results<NoContent, NotFound>> UpdateVacancy(
        Guid id,
        UpdateVacancyRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var isSeen = request.IsSeen!.Value;
        var updated = await db.Vacancies
            .Where(v => v.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsSeen, isSeen), cancellationToken);

        return updated == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

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
