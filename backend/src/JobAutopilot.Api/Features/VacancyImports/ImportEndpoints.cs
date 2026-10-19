using System.ComponentModel.DataAnnotations;
using JobAutopilot.Api.Common;
using JobAutopilot.Core.Import;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.VacancyImports;

public static class ImportEndpoints
{
    public const int DefaultRecentLimit = 10;
    public const int MaxRecentLimit = 50;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").WithTags("Imports");

        group.MapPost("/", StartImport)
            .WithName(nameof(StartImport))
            .RequireRateLimiting(ApiSecurity.ImportRateLimitPolicy);
        group.MapGet("/{id:guid}", GetImport).WithName(nameof(GetImport));
        group.MapGet("/", ListImports).WithName(nameof(ListImports));
        group.MapGet("/searches", ListSearches).WithName(nameof(ListSearches));

        return app;
    }

    /// <summary>
    /// Queues an import of the configured searches (empty body) or of the one search in the body and returns at once
    /// with 202 and the run id; progress via <c>GET /api/imports/{id}</c>. 409 while another import is queued or running.
    /// </summary>
    private static async Task<Results<Accepted<ImportStartedDto>, ProblemHttpResult>> StartImport(
        ImportSearchRequest? request,
        ImportCoordinator coordinator,
        IOptions<ImportOptions> importOptions,
        CancellationToken cancellationToken)
    {
        var options = importOptions.Value;
        IReadOnlyList<VacancySearch> searches = request is null ? options.Searches : [request.ToSearch()];

        var run = await coordinator.TryStartAsync(ImportTrigger.Manual, searches, options.ExcludeTitleTerms, cancellationToken);
        if (run is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "An import is already running.",
                detail: "Only one import can run at a time. Wait until it has finished and try again.");
        }

        return TypedResults.Accepted($"/api/imports/{run.Id}", new ImportStartedDto(run.Id));
    }

    private static async Task<Results<Ok<ImportRunDto>, NotFound>> GetImport(
        Guid id,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var run = await db.ImportRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(ImportRunDto.From(run));
    }

    /// <summary>Most recent runs first.</summary>
    private static async Task<Ok<List<ImportRunDto>>> ListImports(
        [AsParameters] RecentImportsQuery query,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var runs = await db.ImportRuns.AsNoTracking()
            .OrderByDescending(r => r.RequestedAt)
            .Take(query.Limit ?? DefaultRecentLimit)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(runs.ConvertAll(ImportRunDto.From));
    }

    /// <summary>The configured searches (Import:Searches), e.g. for the UI's search filter.</summary>
    private static Ok<List<ConfiguredSearchDto>> ListSearches(IOptions<ImportOptions> importOptions) =>
        TypedResults.Ok(importOptions.Value.Searches.ConvertAll(ConfiguredSearchDto.From));

    public sealed class RecentImportsQuery
    {
        [Range(1, MaxRecentLimit)]
        public int? Limit { get; init; }
    }
}
