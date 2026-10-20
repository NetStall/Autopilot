using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Profile;
using Microsoft.Extensions.Configuration;

namespace JobAutopilot.Tests.Unit.Scoring;

/// <summary>
/// Real postings (<c>Fixtures/vacancies/*.txt</c>: a header with title, company, location, source, remote hint and
/// optionally the source's home-office share (<c>homeOfficePercent</c>), then
/// <c>---</c> and the description) scored like in production: the shipped configuration, the Suchprofil seeded from
/// it, and an invented profile with the shape of the real one (<c>Fixtures/profiles/regression-profile.json</c>).
/// </summary>
internal static class RealWorldFixtures
{
    public static readonly DateOnly Today = new(2026, 10, 20);

    private static readonly Lazy<IConfiguration> Configuration = new(() => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
        .Build());

    public static CandidateProfile Profile { get; } =
        FileCandidateProfileSource.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "profiles", "regression-profile.json")), Today).Profile
        ?? throw new InvalidOperationException("The regression profile must be valid.");

    public static FitOptions Fit { get; } =
        Configuration.Value.GetSection(FitOptions.SectionName).Get<FitOptions>() ?? throw new InvalidOperationException("No Fit section.");

    public static SearchProfile SearchProfile { get; } = SearchProfileSeed.Create(
        Configuration.Value.GetSection(ImportOptions.SectionName).Get<ImportOptions>()!.Searches,
        Configuration.Value.GetSection(ImportOptions.SectionName).Get<ImportOptions>()!.ExcludeTitleTerms,
        ShippedConfig.Relevance(),
        Fit.Penalties,
        Configuration.Value.GetSection(SearchProfileDefaults.SectionName).Get<SearchProfileDefaults>() ?? new SearchProfileDefaults(),
        Profile.Preferences);

    public static VacancyEnricher Enricher()
    {
        var profiles = new FixedProfileSource(Profile);
        var search = new FixedSearchProfileSource(SearchProfile);
        return new VacancyEnricher(
            new ProfileRelevanceScorerProvider(ShippedConfig.Relevance(), profiles, search),
            new RemoteClassifier(ShippedConfig.Remote()),
            new EmployerClassifier(ShippedConfig.Employer()),
            new VacancyFitService(new FitAnalyzer(Fit), profiles, new FixedClock(Today), search));
    }

    public static (Vacancy Vacancy, RemoteLevel RemoteHint) Load(string name)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vacancies", name + ".txt")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var split = text.IndexOf("\n---\n", StringComparison.Ordinal);
        var header = text[..split].Split('\n')
            .Select(line => line.Split(": ", 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);
        var vacancy = new Vacancy
        {
            Id = Guid.NewGuid(),
            Source = Enum.Parse<VacancySource>(header["source"]),
            ExternalId = name,
            Title = header["title"],
            Company = header["company"],
            Location = header["location"],
            Url = "https://example.org/" + name,
            Description = text[(split + 5)..],
            HomeOfficePercent = header.TryGetValue("homeOfficePercent", out var percent) ? int.Parse(percent, System.Globalization.CultureInfo.InvariantCulture) : null,
        };
        return (vacancy, Enum.Parse<RemoteLevel>(header["remoteHint"]));
    }

    /// <summary>Loads and scores one fixture.</summary>
    public static Vacancy Scored(string name)
    {
        var (vacancy, hint) = Load(name);
        Enricher().Apply(vacancy, hint);
        return vacancy;
    }

    private sealed class FixedProfileSource(CandidateProfile profile) : ICandidateProfileSource
    {
        public ProfileSnapshot Current { get; } = new(ProfileStatus.Loaded, profile, [], [], "regression-profile.json", null, 1);
    }

    private sealed class FixedClock(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
    }
}
