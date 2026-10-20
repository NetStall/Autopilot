using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.SearchProfiles;

/// <summary>Values from <c>appsettings.json</c> that only seed the Suchprofil on first start.</summary>
public sealed class SearchProfileDefaults
{
    public const string SectionName = "SearchProfileDefaults";

    public int MinScore { get; set; }

    public int TopN { get; set; } = 20;

    /// <summary>Must-have keywords are configured positive rules with at least this weight; the rest are nice-to-have.</summary>
    public int MustHaveFromWeight { get; set; } = 8;

    public int RemotePreferencePenalty { get; set; } = 10;
}

/// <summary>Builds the first Suchprofil from the old configuration and, once, from <c>preferences</c> in <c>profile.json</c>.</summary>
public static class SearchProfileSeed
{
    public static SearchProfile Create(
        IReadOnlyList<VacancySearch> searches,
        IReadOnlyList<string> titleExclusions,
        RelevanceOptions relevance,
        FitPenalties penalties,
        SearchProfileDefaults defaults,
        Preferences? legacy)
    {
        ArgumentNullException.ThrowIfNull(searches);
        ArgumentNullException.ThrowIfNull(titleExclusions);
        ArgumentNullException.ThrowIfNull(relevance);
        ArgumentNullException.ThrowIfNull(penalties);
        ArgumentNullException.ThrowIfNull(defaults);

        var terms = searches
            .GroupBy(s => s.Keyword.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var region = g.Any(s => s.Location is not null);
                var remote = g.Any(s => s.RemoteOnly || s.Location is null);
                return new SearchTerm
                {
                    Text = g.First().Keyword.Trim(),
                    Scope = region && remote ? SearchTermScope.Both : region ? SearchTermScope.Region : SearchTermScope.Remote,
                };
            })
            .ToList();

        var places = searches
            .Where(s => s.Location is not null)
            .GroupBy(s => s.Location!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new SearchPlace { City = g.Key, RadiusKm = g.Max(s => s.RadiusKm ?? 0) })
            .ToList();

        var profile = new SearchProfile
        {
            SearchTerms = terms,
            Places = places,
            RemoteGermany = searches.Any(s => s.Location is null),
            MustHave = [.. relevance.Positive.Where(r => r.Weight >= defaults.MustHaveFromWeight).Select(StackKeyword.From)],
            NiceToHave = [.. relevance.Positive.Where(r => r.Weight < defaults.MustHaveFromWeight).Select(StackKeyword.From)],
            Exclude = [.. relevance.Negative.Select(StackKeyword.From)],
            TitleExclusions = [.. titleExclusions.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)],
            PublishedWithinDays = searches.Count == 0 ? VacancySearch.DefaultPublishedWithinDays : searches.Max(s => s.PublishedWithinDays),
            Sources = new SearchSources
            {
                Arbeitsagentur = searches.Count == 0 || searches.Any(s => s.Source == VacancySource.Arbeitsagentur),
                Himalayas = searches.Any(s => s.Source == VacancySource.Himalayas),
            },
            MinScore = defaults.MinScore,
            TopN = defaults.TopN,
            Penalties = new SearchPenalties
            {
                GermanPerLevel = penalties.GermanPerLevel,
                Experience = penalties.Experience,
                RequiredGap = penalties.RequiredGap,
                RequiredGapMax = penalties.RequiredGapMax,
                OutsideRegion = penalties.OutsideRegion,
                EmployerType = penalties.AvoidedEmployerType,
                RemotePreference = defaults.RemotePreferencePenalty,
            },
            Agency = EmployerTypePolicy.Allow,
            Temporary = EmployerTypePolicy.Allow,
        };

        if (legacy is not null)
        {
            ApplyLegacy(profile, legacy);
        }

        return profile;
    }

    /// <summary>The <c>preferences</c> part of <c>profile.json</c> (deprecated since step 05b) moves here once.</summary>
    public static void ApplyLegacy(SearchProfile profile, Preferences legacy)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(legacy);
        profile.SalaryMin = legacy.SalaryMin;
        profile.SalaryMax = legacy.SalaryMax;
        profile.EarliestStart = legacy.EarliestStart;
        profile.MaxCommuteMinutes = legacy.MaxCommuteMinutes;
        profile.AdditionalPlaces = [.. legacy.Regions
            .Where(r => !string.IsNullOrWhiteSpace(r) && !string.Equals(r.Trim(), "Remote", StringComparison.OrdinalIgnoreCase))
            .Where(r => !profile.Places.Any(p => string.Equals(p.City, r.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        profile.RemotePreference = legacy.Remote switch
        {
            "Remote" => RemotePreference.FullRemoteOnly,
            "Hybrid" => RemotePreference.AtLeastHybrid,
            _ => RemotePreference.Any,
        };
        profile.Agency = legacy.AvoidEmployerTypes.Contains(EmployerType.Agency) ? EmployerTypePolicy.RankLower : EmployerTypePolicy.Allow;
        profile.Temporary = legacy.AvoidEmployerTypes.Contains(EmployerType.Temporary) ? EmployerTypePolicy.RankLower : EmployerTypePolicy.Allow;
    }

    /// <summary>Whether <c>profile.json</c> still has a non-empty <c>preferences</c> part.</summary>
    public static bool HasLegacyPreferences(CandidateProfile? profile) =>
        profile?.Preferences is { } p
        && (p.SalaryMin is not null || p.SalaryMax is not null || p.Regions.Count > 0 || p.EarliestStart is not null
            || p.AvoidEmployerTypes.Count > 0 || p.MaxCommuteMinutes is not null || p.Remote != "Any");
}

/// <summary>The current Suchprofil; <see cref="Version"/> increases with every save.</summary>
public sealed record SearchProfileSnapshot(SearchProfile Profile, int Version, DateTimeOffset? UpdatedAt);

/// <summary>Gives the current Suchprofil (cached; replaced on save).</summary>
public interface ISearchProfileSource
{
    SearchProfileSnapshot Current { get; }
}

/// <summary>A fixed Suchprofil — tests, preview.</summary>
public sealed class FixedSearchProfileSource(SearchProfile profile, int version = 1) : ISearchProfileSource
{
    public SearchProfileSnapshot Current { get; } = new(profile, version, null);
}
