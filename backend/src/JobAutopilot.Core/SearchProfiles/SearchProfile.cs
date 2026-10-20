using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.SearchProfiles;

/// <summary>Where a search term is used: around the places, remote in all of Germany, or both.</summary>
public enum SearchTermScope
{
    Both,
    Region,
    Remote,
}

/// <summary>What the list prefers regarding home office.</summary>
public enum RemotePreference
{
    /// <summary>„egal"</summary>
    Any,

    /// <summary>„mindestens Hybrid" — on-site only costs points.</summary>
    AtLeastHybrid,

    /// <summary>„nur voll remote" — everything else costs points.</summary>
    FullRemoteOnly,
}

/// <summary>How agencies and temporary work are treated.</summary>
public enum EmployerTypePolicy
{
    /// <summary>No penalty (still after direct employers at the same score).</summary>
    Allow,

    /// <summary>A ranking penalty (<see cref="SearchPenalties.EmployerType"/>).</summary>
    RankLower,

    /// <summary>Hidden from the list unless the employer-type filter asks for it.</summary>
    Hide,
}

public sealed class SearchTerm
{
    public string Text { get; set; } = string.Empty;

    public SearchTermScope Scope { get; set; } = SearchTermScope.Both;
}

public sealed class SearchPlace
{
    public string City { get; set; } = string.Empty;

    public int RadiusKm { get; set; } = 50;
}

/// <summary>A keyword of the stack lists — the same fields as a relevance rule.</summary>
public sealed class StackKeyword
{
    public string Name { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    /// <summary>Points; title matches count <c>TitleFactor</c> times.</summary>
    public int Weight { get; set; }

    /// <summary>Related skills share a cap (<c>Relevance:GroupCaps</c>), e.g. <c>dotnet</c>.</summary>
    public string? Group { get; set; }

    /// <summary>Must-have only: the core stack; softens excluded alternatives next to it („Java oder C#").</summary>
    public bool Core { get; set; }

    public bool TitleOnly { get; set; }

    public bool MatchCase { get; set; }

    /// <summary>Exclusions only: weakened when the core stack is found at least as prominently.</summary>
    public bool SoftenedByCore { get; set; }

    /// <summary>Exclusions only: text of the reason, e.g. „Java als Hauptsprache".</summary>
    public string? Reason { get; set; }

    public RelevanceRule ToRule()
    {
        var rule = new RelevanceRule
        {
            Name = Name.Trim(),
            Weight = Weight,
            Group = string.IsNullOrWhiteSpace(Group) ? null : Group.Trim(),
            Core = Core,
            TitleOnly = TitleOnly,
            MatchCase = MatchCase,
            SoftenedByCore = SoftenedByCore,
            Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim(),
        };
        rule.Terms.AddRange(Aliases.Prepend(Name).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.Ordinal));
        return rule;
    }

    public static StackKeyword From(RelevanceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new StackKeyword
        {
            Name = rule.Name,
            Aliases = [.. rule.Terms.Where(t => !string.Equals(t, rule.Name, StringComparison.Ordinal))],
            Weight = rule.Weight,
            Group = rule.Group,
            Core = rule.Core,
            TitleOnly = rule.TitleOnly,
            MatchCase = rule.MatchCase,
            SoftenedByCore = rule.SoftenedByCore,
            Reason = rule.Reason,
        };
    }
}

public sealed class SearchPenalties
{
    public int GermanPerLevel { get; set; } = 8;

    /// <summary>More years than mine — only „Senior" in the title.</summary>
    public int Experience { get; set; } = 8;

    /// <summary>More years than mine with a strong signal: „langjährige Erfahrung", „5+ Jahre", „Lead", Senior in the requirements.</summary>
    public int ExperienceStrong { get; set; } = 10;

    /// <summary>Per gap in a must-have section („Was du mitbringst", „Anforderungen", „(Must-have)").</summary>
    public int MustHaveGap { get; set; } = 15;

    /// <summary>Per gap required elsewhere (title, tasks, no section found).</summary>
    public int RequiredGap { get; set; } = 6;

    /// <summary>Per gap in a stack section without a marker („Sprachen &amp; Technologien", „Tech-Stack").</summary>
    public int LikelyRequiredGap { get; set; } = 10;

    /// <summary>Per gap that is only nice to have („Ein Plus", „von Vorteil", „wünschenswert").</summary>
    public int NiceToHaveGap { get; set; } = 4;

    /// <summary>Must-have gaps together at most this much.</summary>
    public int MustHaveGapMax { get; set; } = 30;

    /// <summary>All other gaps together at most this much.</summary>
    public int RequiredGapMax { get; set; } = 18;

    public int OutsideRegion { get; set; } = 15;

    /// <summary>Outside my places, on site only or at most 60 % home office stated: practically not reachable.</summary>
    public int Unreachable { get; set; } = 35;

    /// <summary>Outside my places with an unknown home-office share.</summary>
    public int OutsideRegionUnknownShare { get; set; } = 20;

    /// <summary>For agencies / temporary work with policy <see cref="EmployerTypePolicy.RankLower"/>.</summary>
    public int EmployerType { get; set; } = 10;

    /// <summary>When the remote preference is not met.</summary>
    public int RemotePreference { get; set; } = 10;
}

public sealed class SearchSources
{
    public bool Arbeitsagentur { get; set; } = true;

    public bool Himalayas { get; set; } = true;
}

/// <summary>
/// „Suchprofil" — what Vitalii is looking for (preferences), stored in the database and edited in the UI.
/// Facts about him stay in <c>profile.json</c>; <c>appsettings.json</c> only seeds this on first start.
/// </summary>
public sealed class SearchProfile
{
    public List<SearchTerm> SearchTerms { get; set; } = [];

    /// <summary>Places searched with their radius; also count as „my region" for the fit check.</summary>
    public List<SearchPlace> Places { get; set; } = [];

    /// <summary>Places that count as my region in the fit check but get no search of their own.</summary>
    public List<string> AdditionalPlaces { get; set; } = [];

    /// <summary>„Remote deutschlandweit": remote searches without location (Arbeitsagentur, Himalayas).</summary>
    public bool RemoteGermany { get; set; } = true;

    public RemotePreference RemotePreference { get; set; } = RemotePreference.Any;

    public List<StackKeyword> MustHave { get; set; } = [];

    public List<StackKeyword> NiceToHave { get; set; } = [];

    public List<StackKeyword> Exclude { get; set; } = [];

    /// <summary>Vacancies whose title contains one of these are not stored (Ausbildung, Praktikum …).</summary>
    public List<string> TitleExclusions { get; set; } = [];

    public EmployerTypePolicy Agency { get; set; } = EmployerTypePolicy.Allow;

    public EmployerTypePolicy Temporary { get; set; } = EmployerTypePolicy.RankLower;

    public int PublishedWithinDays { get; set; } = 30;

    public SearchSources Sources { get; set; } = new();

    /// <summary>Default of the list's „Mindestbewertung".</summary>
    public int MinScore { get; set; }

    /// <summary>N of „Nur Top N".</summary>
    public int TopN { get; set; } = 20;

    /// <summary>Default of the list filter „Deutsch erforderlich" while the URL has none; empty = all.</summary>
    public List<Vacancies.RequiredGerman> GermanFilter { get; set; } = [];

    /// <summary>„Nicht erreichbare Stellen ausblenden": the list leaves out vacancies the fit check marks as not reachable.</summary>
    public bool HideUnreachable { get; set; } = true;

    public SearchPenalties Penalties { get; set; } = new();

    public int? SalaryMin { get; set; }

    public int? SalaryMax { get; set; }

    /// <summary><c>YYYY-MM-DD</c>.</summary>
    public string? EarliestStart { get; set; }

    public int? MaxCommuteMinutes { get; set; }

    /// <summary>Employer types hidden from the list.</summary>
    public IReadOnlyList<EmployerType> HiddenEmployerTypes() =>
        [.. new[] { (EmployerType.Agency, Agency), (EmployerType.Temporary, Temporary) }
            .Where(p => p.Item2 == EmployerTypePolicy.Hide).Select(p => p.Item1)];

    /// <summary>Employer types ranked lower.</summary>
    public IReadOnlyList<EmployerType> RankedLowerEmployerTypes() =>
        [.. new[] { (EmployerType.Agency, Agency), (EmployerType.Temporary, Temporary) }
            .Where(p => p.Item2 == EmployerTypePolicy.RankLower).Select(p => p.Item1)];
}
