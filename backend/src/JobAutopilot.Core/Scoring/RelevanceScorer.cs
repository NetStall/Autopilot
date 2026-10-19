using System.Globalization;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Scoring;

/// <summary>The relevance of one vacancy: score 0–100, level and the reasons behind it (German, for the UI).</summary>
public sealed record RelevanceResult(int Score, RelevanceLevel Level, IReadOnlyList<string> Reasons);

/// <summary>
/// Scores a vacancy against the profile with the keyword rules of <see cref="RelevanceOptions"/>. Pure and
/// deterministic: the same title, description and options always give the same result.
/// </summary>
public sealed class RelevanceScorer
{
    private readonly RelevanceOptions _options;
    private readonly CompiledRule[] _positive;
    private readonly CompiledRule[] _negative;

    /// <param name="additionalPositive">More positive rules, e.g. from the candidate profile's skills. A rule whose
    /// name or terms are already covered by a configured rule is ignored, so a skill never counts twice.</param>
    public RelevanceScorer(RelevanceOptions options, IEnumerable<RelevanceRule>? additionalPositive = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        var configured = options.Positive;
        var known = new HashSet<string>(
            configured.SelectMany(r => r.Terms.Append(r.Name)).Select(t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var extra = (additionalPositive ?? [])
            .Where(r => !known.Contains(r.Name.Trim()) && !r.Terms.Any(t => known.Contains(t.Trim())));
        _positive = [.. configured.Concat(extra).Select(r => new CompiledRule(r))];
        _negative = [.. options.Negative.Select(r => new CompiledRule(r))];
    }

    public RelevanceResult Score(string title, string? description)
    {
        ArgumentNullException.ThrowIfNull(title);
        var contributions = new List<(double Points, string Reason)>();
        var groupTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var anyPositive = false;

        var corePlace = Place.None;
        foreach (var rule in _positive)
        {
            var place = rule.Find(title, description);
            if (place == Place.None)
            {
                continue;
            }

            if (rule.Rule.Core && place > corePlace)
            {
                corePlace = place;
            }

            var points = rule.Rule.Weight * Factor(place);
            contributions.Add((points, $"{rule.Rule.Name}{Where(place)} ✓ ({Signed(points)})"));
            anyPositive = true;
            if (rule.Rule.Group is { Length: > 0 } group)
            {
                groupTotals[group] = groupTotals.GetValueOrDefault(group) + points;
            }
        }

        // Related skills do not stack without limit: ".NET" in the title plus C# plus ASP.NET is one strong signal.
        foreach (var (group, total) in groupTotals)
        {
            if (_options.GroupCaps.TryGetValue(group, out var cap) && total > cap)
            {
                contributions.Add((cap - total, $"Gruppe {group} gedeckelt auf {cap} ({Signed(cap - total)})"));
            }
        }

        foreach (var rule in _negative)
        {
            var place = rule.Find(title, description);
            if (place == Place.None)
            {
                continue;
            }

            // An alternative next to C#/.NET found at least as prominently ("Java oder C#", "Python von Vorteil" in a
            // .NET job) is no reason to drop the vacancy; as the main topic ("Java-Entwickler") it is.
            var softened = rule.Rule.SoftenedByCore && corePlace >= place;
            var points = -rule.Rule.Weight * Factor(place) * (softened ? _options.SoftenedFactor : 1);
            var reason = softened
                ? $"{rule.Rule.Name} neben C#/.NET{Where(place)} – abgeschwächt ({Signed(points)})"
                : $"{rule.Rule.Reason ?? rule.Rule.Name}{Where(place)} ✗ ({Signed(points)})";
            contributions.Add((points, reason));
        }

        var raw = _options.BaseScore + contributions.Sum(c => c.Points);
        var score = (int)Math.Round(Math.Clamp(raw, 0, 100), MidpointRounding.AwayFromZero);

        var reasons = contributions
            .OrderByDescending(c => Math.Abs(c.Points))
            .Select(c => c.Reason)
            .ToList();
        if (string.IsNullOrWhiteSpace(description))
        {
            reasons.Add("Nur der Titel bewertet (noch keine Beschreibung)");
        }

        var level = LevelFor(score);
        if (string.IsNullOrWhiteSpace(description) && anyPositive && level == RelevanceLevel.Irrelevant
            && !contributions.Any(c => c.Points < 0))
        {
            // Only the title is known yet: a developer title without negatives stays visible until the text arrives.
            level = RelevanceLevel.Maybe;
        }

        return new RelevanceResult(score, level, reasons);
    }

    public RelevanceLevel LevelFor(int score) =>
        score >= _options.RelevantFrom ? RelevanceLevel.Relevant
        : score >= _options.MaybeFrom ? RelevanceLevel.Maybe
        : RelevanceLevel.Irrelevant;

    private double Factor(Place place) => place == Place.Title ? _options.TitleFactor : 1;

    private static string Where(Place place) => place == Place.Title ? " im Titel" : string.Empty;

    private static string Signed(double points)
    {
        var rounded = (int)Math.Round(points, MidpointRounding.AwayFromZero);
        return (rounded >= 0 ? "+" : string.Empty) + rounded.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Where a rule was found; the title counts more than the description.</summary>
    private enum Place
    {
        None = 0,
        Description = 1,
        Title = 2,
    }

    private sealed class CompiledRule(RelevanceRule rule)
    {
        private readonly TermMatcher _matcher = new(rule.Terms, rule.MatchCase);

        public RelevanceRule Rule { get; } = rule;

        public Place Find(string title, string? description)
        {
            if (_matcher.FindFirst(title) is not null)
            {
                return Place.Title;
            }

            return !Rule.TitleOnly && _matcher.FindFirst(description) is not null ? Place.Description : Place.None;
        }
    }
}
