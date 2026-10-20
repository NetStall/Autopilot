using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Statistics;

public sealed record GermanLevelCountDto(RequiredGerman Level, string Label, int Count, double Percent);

/// <summary>The distribution for one value of a breakdown (e.g. remote level „FullRemote").</summary>
public sealed record GermanBreakdownDto(string Key, int Total, IReadOnlyList<GermanLevelCountDto> Levels);

/// <summary>
/// How many relevant vacancies ask for which German: overall and by remote level and source. „Relevant" = what the
/// list shows by default: Passt + Vielleicht, rank ≥ the Suchprofil's Mindestbewertung, not dismissed, not expired.
/// </summary>
public sealed record GermanStatisticsDto(
    int MinScore,
    int Total,
    IReadOnlyList<GermanLevelCountDto> Overall,
    IReadOnlyList<GermanBreakdownDto> ByRemoteLevel,
    IReadOnlyList<GermanBreakdownDto> BySource);

public static class StatisticsEndpoints
{
    /// <summary>Display order: the strongest requirement first.</summary>
    private static readonly RequiredGerman[] Order =
        [RequiredGerman.C1Plus, RequiredGerman.B2, RequiredGerman.B1OrLower, RequiredGerman.None, RequiredGerman.Unknown];

    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/statistics/german", German).WithTags("Statistics").WithName("GermanStatistics");
        return app;
    }

    private static async Task<Ok<GermanStatisticsDto>> German(
        AppDbContext db, ISearchProfileSource searchProfiles, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var profile = searchProfiles.Current.Profile;
        var filter = new VacancyFilter(
            RelevanceLevels: [RelevanceLevel.Relevant, RelevanceLevel.Maybe],
            MinScore: profile.MinScore,
            NotExpiredAt: timeProvider.GetUtcNow(),
            HiddenEmployerTypes: profile.HiddenEmployerTypes());
        var rows = await VacancyEndpoints.Filtered(db, filter)
            .GroupBy(v => new { v.RequiredGerman, v.RemoteLevel, v.Source })
            .Select(g => new { g.Key.RequiredGerman, g.Key.RemoteLevel, g.Key.Source, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var all = rows.Select(r => (r.RequiredGerman, r.Count)).ToList();
        return TypedResults.Ok(new GermanStatisticsDto(
            profile.MinScore,
            all.Sum(r => r.Count),
            Distribution(all),
            [.. rows.GroupBy(r => r.RemoteLevel).OrderBy(g => g.Key).Select(g => Breakdown(g.Key.ToString(), g.Select(r => (r.RequiredGerman, r.Count))))],
            [.. rows.GroupBy(r => r.Source).OrderBy(g => g.Key).Select(g => Breakdown(g.Key.ToString(), g.Select(r => (r.RequiredGerman, r.Count))))]));
    }

    private static GermanBreakdownDto Breakdown(string key, IEnumerable<(RequiredGerman Level, int Count)> rows)
    {
        var list = rows.ToList();
        return new GermanBreakdownDto(key, list.Sum(r => r.Count), Distribution(list));
    }

    private static List<GermanLevelCountDto> Distribution(List<(RequiredGerman Level, int Count)> rows)
    {
        var total = rows.Sum(r => r.Count);
        return [.. Order.Select(level =>
        {
            var count = rows.Where(r => r.Level == level).Sum(r => r.Count);
            return new GermanLevelCountDto(level, GermanRequirement.Label(level), count, total == 0 ? 0 : Math.Round(100.0 * count / total, 1));
        })];
    }
}
