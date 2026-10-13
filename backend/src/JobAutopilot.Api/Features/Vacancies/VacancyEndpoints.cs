using System.ComponentModel.DataAnnotations;
using JobAutopilot.Api.Common;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Vacancies;

public static partial class VacancyEndpoints
{
    private const string LogCategory = "JobAutopilot.Api.Vacancies";

    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapVacancyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vacancies").WithTags("Vacancies");

        group.MapGet("/", ListVacancies).WithName(nameof(ListVacancies));
        group.MapGet("/{id:guid}", GetVacancy).WithName(nameof(GetVacancy));
        group.MapPost("/", CreateVacancy).WithName(nameof(CreateVacancy));

        return app;
    }

    /// <summary>Newest first, by the time the vacancy entered the system.</summary>
    private static async Task<Ok<PagedResponse<VacancyDto>>> ListVacancies(
        AppDbContext db,
        CancellationToken cancellationToken,
        [Range(1, int.MaxValue)] int page = 1,
        [Range(1, MaxPageSize)] int pageSize = DefaultPageSize)
    {
        var query = db.Vacancies.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);
        var vacancies = await query
            .OrderByDescending(v => v.FetchedAt)
            .ThenByDescending(v => v.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = vacancies.Select(v => v.ToDto()).ToList();
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Created manual vacancy {VacancyId}")]
    private static partial void LogManualVacancyCreated(ILogger logger, Guid vacancyId);
}
