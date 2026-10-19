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

    public RelevanceScorer(RelevanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _positive = [.. options.Positive.Select(r => new CompiledRule(r))];
        _negative = [.. options.Negative.Select(r => new CompiledRule(r))];
    }

    public RelevanceResult Score(string title, string? description)
    {
        ArgumentNullException.ThrowIfNull(title);
        var contributions = new List<(double Points, string Reason)>();

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

        return new RelevanceResult(score, LevelFor(score), reasons);
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
