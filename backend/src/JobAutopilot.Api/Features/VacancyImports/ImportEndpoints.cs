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
        Core.SearchProfiles.ISearchProfileSource searchProfiles,
        CancellationToken cancellationToken)
    {
        // Without a body: the searches generated from the Suchprofil (search terms × places + remote).
        var (configured, exclusions) = SearchProfiles.SearchProfileImports.Current(searchProfiles);
        IReadOnlyList<VacancySearch> searches = request is null ? configured : [request.ToSearch()];

        var run = await coordinator.TryStartAsync(ImportTrigger.Manual, searches, exclusions, cancellationToken);
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

    /// <summary>The searches generated from the Suchprofil, e.g. for the UI's search filter.</summary>
    private static Ok<List<ConfiguredSearchDto>> ListSearches(Core.SearchProfiles.ISearchProfileSource searchProfiles) =>
        TypedResults.Ok(SearchProfiles.SearchProfileImports.Current(searchProfiles).Searches.Select(ConfiguredSearchDto.From).ToList());

    public sealed class RecentImportsQuery
    {
        [Range(1, MaxRecentLimit)]
        public int? Limit { get; init; }
    }
}
