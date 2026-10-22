using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.DailyRun;

/// <summary>One search (query × place or remote) and what it brought in the window.</summary>
public sealed record SearchCoverageDto(string Name, VacancySource? Source, bool Configured, int Vacancies, int InTop50);

/// <summary>Does the search find what I want? Per query: vacancies of the last N days and how many reached the Top 50.</summary>
public sealed record CoverageDto(int Days, int Vacancies, IReadOnlyList<SearchCoverageDto> Searches, IReadOnlyList<string> BringNothing);

public static class DailyRunEndpoints
{
    public static IEndpointRouteBuilder MapDailyRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/daily-run").WithTags("Daily run");
        group.MapGet("/", (DailyRunCoordinator coordinator) => TypedResults.Ok(coordinator.Status)).WithName("GetDailyRun");
        group.MapPost("/", StartDailyRun).WithName(nameof(StartDailyRun));
        group.MapDelete("/current", (DailyRunCoordinator coordinator) =>
        {
            coordinator.Cancel();
            return TypedResults.NoContent();
        }).WithName("CancelDailyRun");
        app.MapGet("/api/statistics/coverage", Coverage).WithTags("Statistics").WithName("SearchCoverage");
        return app;
    }

    /// <summary>Starts the run (202) or merges into the one already queued or running (also 202, same status).</summary>
    private static Results<Accepted<DailyRunStatusDto>, ValidationProblem> StartDailyRun(DailyRunCoordinator coordinator, int top = 100)
    {
        if (top is < 1 or > 500)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["top"] = ["top must be between 1 and 500."] });
        }

        coordinator.Request(top);
        return TypedResults.Accepted("/api/daily-run", coordinator.Status);
    }

    private static async Task<Results<Ok<CoverageDto>, ValidationProblem>> Coverage(
        AppDbContext db, ISearchProfileSource searchProfiles, TimeProvider timeProvider, CancellationToken cancellationToken, int days = 7)
    {
        if (days is < 1 or > 90)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["days"] = ["days must be between 1 and 90."] });
        }

        var now = timeProvider.GetUtcNow();
        var profile = searchProfiles.Current.Profile;
        var top50 = (await VacancyEndpoints.Filtered(db, new VacancyFilter(NotExpiredAt: now, HiddenEmployerTypes: profile.HiddenEmployerTypes(), HideFiltered: true))
            .ApplySort(VacancySort.Relevance).Take(50).Select(v => v.Id).ToListAsync(cancellationToken)).ToHashSet();
        var since = now.AddDays(-days);
        var recent = await db.Vacancies.AsNoTracking().Where(v => v.FetchedAt >= since)
            .Select(v => new { v.Id, v.Source, v.MatchedSearches }).ToListAsync(cancellationToken);

        var (configured, _) = SearchProfiles.SearchProfileImports.Current(searchProfiles);
        var rows = recent.SelectMany(v => v.MatchedSearches.Select(name => (Name: name, v.Source, v.Id)))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Source: g.First().Source, Count: g.Select(r => r.Id).Distinct().Count(), Top: g.Select(r => r.Id).Distinct().Count(top50.Contains)), StringComparer.OrdinalIgnoreCase);

        var searches = configured.Select(s => s.Name).Union(rows.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(name => rows.TryGetValue(name, out var r)
                ? new SearchCoverageDto(name, r.Source, configured.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)), r.Count, r.Top)
                : new SearchCoverageDto(name, null, true, 0, 0))
            .OrderByDescending(s => s.InTop50).ThenByDescending(s => s.Vacancies).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // „Bringen nichts": configured queries without a single vacancy, then those without one in the Top 50.
        var nothing = searches.Where(s => s.Configured).OrderBy(s => s.Vacancies).ThenBy(s => s.InTop50).Where(s => s.InTop50 == 0).Take(5).Select(s => s.Name).ToList();
        return TypedResults.Ok(new CoverageDto(days, recent.Count, searches, nothing));
    }
}
