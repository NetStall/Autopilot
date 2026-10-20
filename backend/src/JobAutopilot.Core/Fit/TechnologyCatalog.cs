using System.Text.Json;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Core.Fit;

/// <summary>One technology a posting may ask for: frameworks, UI libraries, ORMs, protocols, cloud services, domain terms.</summary>
/// <see cref="Parent"/>: the broader technology („AWS" for „AWS Lambda") — when that is already asked for, the part is not a second gap.
public sealed record Technology(string Name, IReadOnlyList<string> Terms, string Category, bool MatchCase, string? Parent = null)
{
    public IEnumerable<string> AllNames() => Terms.Prepend(Name);
}

/// <summary>
/// The maintained list of common technologies (<c>Fit/technologies.json</c>, embedded). A term found in a posting
/// that is not in my profile is a requirement to check — a gap, never a match — and a keyword of the Suchprofil that
/// names one of them only scores when the profile has it (<see cref="ProfileRelevanceScorerProvider"/>).
/// </summary>
public static class TechnologyCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<IReadOnlyList<Technology>> Loaded = new(Load);

    public static IReadOnlyList<Technology> All => Loaded.Value;

    /// <summary>Whether a name or term is a known technology (case-insensitive, ignoring spaces and dashes).</summary>
    public static bool Contains(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        var key = SkillTerms.Key(term);
        return Keys.Value.Contains(key);
    }

    private static readonly Lazy<HashSet<string>> Keys = new(() =>
        [.. All.SelectMany(t => t.AllNames()).Select(SkillTerms.Key)]);

    private sealed record Entry(string Name, List<string> Terms, string Category, bool MatchCase, string? Parent);

    private static IReadOnlyList<Technology> Load()
    {
        using var stream = typeof(TechnologyCatalog).Assembly.GetManifestResourceStream("JobAutopilot.Core.Fit.technologies.json")
            ?? throw new InvalidOperationException("The technology dictionary is not embedded.");
        var entries = JsonSerializer.Deserialize<List<Entry>>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The technology dictionary is empty.");
        return [.. entries.Select(e => new Technology(e.Name, e.Terms, e.Category, e.MatchCase, e.Parent))];
    }

    /// <summary>A matcher per technology, compiled once.</summary>
    internal static IReadOnlyList<(Technology Technology, TermMatcher Matcher)> Matchers { get; } =
        [.. All.Select(t => (t, new TermMatcher(t.AllNames(), t.MatchCase)))];
}
