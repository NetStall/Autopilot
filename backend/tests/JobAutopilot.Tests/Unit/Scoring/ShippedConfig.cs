using JobAutopilot.Core.Scoring;
using Microsoft.Extensions.Configuration;

namespace JobAutopilot.Tests.Unit.Scoring;

/// <summary>The rules from the API's real <c>appsettings.json</c> — the tests check what is actually shipped.</summary>
internal static class ShippedConfig
{
    private static readonly Lazy<IConfiguration> Configuration = new(() => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
        .Build());

    public static RelevanceOptions Relevance() =>
        Configuration.Value.GetSection(RelevanceOptions.SectionName).Get<RelevanceOptions>()
        ?? throw new InvalidOperationException("No Relevance section in appsettings.json.");

    public static RemoteOptions Remote() =>
        Configuration.Value.GetSection(RemoteOptions.SectionName).Get<RemoteOptions>()
        ?? throw new InvalidOperationException("No Remote section in appsettings.json.");

    public static VacancyEnricher Enricher() => new(new RelevanceScorer(Relevance()), new RemoteClassifier(Remote()));
}
