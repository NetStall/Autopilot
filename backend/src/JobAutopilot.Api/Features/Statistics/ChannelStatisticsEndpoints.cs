using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Statistics;

public sealed record ChannelCountDto(ApplyChannel Channel, int Count, double Percent);

/// <summary>One of the top e-mail-ready vacancies: the detected address is only shown, never contacted.</summary>
public sealed record EmailReadyDto(Guid VacancyId, string Title, string Company, int? RankScore, string Email, string? ContactName, ApplySource Source);

/// <summary>
/// Rules vs. the local model on the vacancies that have a v4 extraction: same channel, an address found by only one
/// side, both with different addresses, and addresses of the model dropped because the text does not contain them.
/// </summary>
public sealed record ChannelComparisonDto(int WithAi, int SameChannel, int BothSameEmail, int RulesOnlyEmail, int AiOnlyEmail, int DifferentEmail, int AiEmailRejected);

/// <summary>„Bewerbungswege" (08a) for the current Top N of the visible list (0 = all visible).</summary>
public sealed record ChannelStatisticsDto(
    int Top, int Vacancies, IReadOnlyList<ChannelCountDto> Channels, int EmailReady, double EmailReadyPercent,
    IReadOnlyList<EmailReadyDto> TopEmailReady, ChannelComparisonDto Comparison);

public static class ChannelStatisticsEndpoints
{
    private static readonly ApplyChannel[] Order = [ApplyChannel.Email, ApplyChannel.Portal, ApplyChannel.Form, ApplyChannel.External, ApplyChannel.Unknown];

    public static IEndpointRouteBuilder MapChannelStatisticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/statistics/channels", Channels).WithTags("Statistics").WithName("ChannelStatistics");
        return app;
    }

    private static async Task<Results<Ok<ChannelStatisticsDto>, ValidationProblem>> Channels(
        AppDbContext db, ISearchProfileSource searchProfiles, TimeProvider timeProvider, CancellationToken cancellationToken, int top = 100)
    {
        if (top is < 0 or > 5000)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["top"] = ["top must be between 0 (all) and 5000."] });
        }

        // The list as I see it: not dismissed, not expired, hidden types and unreachable ones out, by rank.
        var profile = searchProfiles.Current.Profile;
        var filter = new VacancyFilter(
            NotExpiredAt: timeProvider.GetUtcNow(), HiddenEmployerTypes: profile.HiddenEmployerTypes(), HideUnreachable: profile.HideUnreachable);
        var sorted = VacancyEndpoints.Filtered(db, filter).ApplySort(VacancySort.Relevance);
        var vacancies = await (top > 0 ? sorted.Take(top) : sorted)
            .Select(v => new { v.Id, v.Title, v.Company, v.RankScore, v.Description, v.Url, v.ExtractionJson })
            .ToListAsync(cancellationToken);

        var rows = vacancies.Select(v =>
        {
            var ai = StoredExtraction.ForDescription(v.ExtractionJson, v.Description)?.Result.Application;
            var rules = HowToApplyDetector.Detect(v.Description, v.Url);
            var resolved = HowToApplyDetector.Resolve(v.Description, v.Url, ai);
            return (Vacancy: v, Rules: rules, Ai: ai, Resolved: resolved, Ready: resolved.EmailReady(!string.IsNullOrWhiteSpace(v.Description)));
        }).ToList();

        double Percent(int count) => rows.Count == 0 ? 0 : Math.Round(100.0 * count / rows.Count, 1);
        var channels = Order.Select(c => rows.Count(r => r.Resolved.Channel == c)).Select((count, i) => new ChannelCountDto(Order[i], count, Percent(count))).ToList();
        var ready = rows.Where(r => r.Ready).ToList();

        var withAi = rows.Where(r => r.Ai is not null).ToList();
        var comparison = new ChannelComparisonDto(
            withAi.Count,
            withAi.Count(r => r.Ai!.Channel == r.Rules.Channel),
            withAi.Count(r => r.Rules.Email is not null && r.Rules.Email == r.Ai!.Email),
            withAi.Count(r => r.Rules.Email is not null && r.Ai!.Email is null),
            withAi.Count(r => r.Rules.Email is null && r.Ai!.Email is not null),
            withAi.Count(r => r.Rules.Email is not null && r.Ai!.Email is not null && r.Rules.Email != r.Ai.Email),
            withAi.Count(r => r.Ai!.EmailRejected));

        return TypedResults.Ok(new ChannelStatisticsDto(
            top, rows.Count, channels, ready.Count, Percent(ready.Count),
            [.. ready.Take(10).Select(r => new EmailReadyDto(r.Vacancy.Id, r.Vacancy.Title, r.Vacancy.Company, r.Vacancy.RankScore, r.Resolved.Email!, r.Resolved.ContactName, r.Resolved.Source))],
            comparison));
    }
}
