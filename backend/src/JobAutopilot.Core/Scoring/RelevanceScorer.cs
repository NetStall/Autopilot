using System.Globalization;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Scoring;

/// <summary>The relevance of one vacancy: score 0–100, level and the reasons behind it (German, for the UI).</summary>
public sealed record RelevanceResult(int Score, RelevanceLevel Level, IReadOnlyList<string> Reasons);

/// <summary>
/// Scores a vacancy against the profile with the keyword rules of <see cref="RelevanceOptions"/>. A match found only
/// in a nice-to-have section counts less (<see cref="RelevanceOptions.NiceToHaveFactor"/>); basics share one small
/// group cap. Pure and deterministic: the same title, description and options always give the same result.
/// </summary>
public sealed class RelevanceScorer
{
    private readonly RelevanceOptions _options;
    private readonly CompiledRule[] _positive;
    private readonly CompiledRule[] _negative;
    private readonly Func<string, RequirementSections> _sections;

    /// <param name="additionalPositive">More positive rules, e.g. from the candidate profile's skills. A rule whose
    /// name or terms are already covered by a configured rule is ignored, so a skill never counts twice.</param>
    /// <param name="sections">Splits a description into sections; default: the default headings of <see cref="FitOptions"/>.</param>
    public RelevanceScorer(
        RelevanceOptions options,
        IEnumerable<RelevanceRule>? additionalPositive = null,
        Func<string, RequirementSections>? sections = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        var configured = options.Positive;
        var known = new HashSet<string>(
            configured.SelectMany(r => r.Terms.Append(r.Name)).Select(t => t.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var extra = (additionalPositive ?? [])
            .Where(r => !known.Contains(r.Name.Trim()) && !r.Terms.Any(t => known.Contains(t.Trim())));
        var basics = new HashSet<string>(options.BasicSkills.Select(SkillTerms.Key), StringComparer.Ordinal);
        _positive = [.. configured.Concat(extra).Select(r => new CompiledRule(r, basics.Contains(SkillTerms.Key(r.Name))))];
        _negative = [.. options.Negative.Select(r => new CompiledRule(r, isBasic: false))];
        _sections = sections ?? DefaultSections;
    }

    private static readonly Func<string, RequirementSections> DefaultSections = RequirementSections.Parser(new FitOptions());

    public RelevanceResult Score(string title, string? description)
    {
        ArgumentNullException.ThrowIfNull(title);
        var contributions = new List<(double Points, string Reason)>();
        var groupTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var anyPositive = false;

        var corePlace = Place.None;
        var sections = string.IsNullOrWhiteSpace(description) ? null : _sections(description);
        var found = new List<(CompiledRule Rule, double Points)>();
        foreach (var rule in _positive)
        {
            var (place, matched) = rule.Find(title, description);
            if (place == Place.None)
            {
                continue;
            }

            if (rule.Rule.Core && place > corePlace)
            {
                corePlace = place;
            }

            // „Kenntnisse in Bootstrap" under „Dein Profil (Nice to Have)" is a weaker signal than the same under „Was du mitbringst".
            var onlyNiceToHave = place == Place.Description && sections is not null && rule.OnlyIn(description!, sections, SectionKind.NiceToHave);
            var points = rule.Rule.Weight * Factor(place) * (onlyNiceToHave ? _options.NiceToHaveFactor : 1);
            contributions.Add((points, $"{rule.Rule.Name}{Matched(rule.Rule.Name, matched)}{Where(place)}{(onlyNiceToHave ? " (nur „Plus“)" : string.Empty)} ✓ ({Signed(points)})"));
            anyPositive = true;
            found.Add((rule, points));
        }

        // Basics (HTML/CSS, Git, Scrum, REST …) are in almost every posting: together they count at most the basics
        // cap. They are scaled down first and then still count in their own group, so the cap can only lower a score.
        var basicsTotal = found.Where(f => f.Rule.IsBasic).Sum(f => f.Points);
        var basicsScale = 1.0;
        if (_options.GroupCaps.TryGetValue(_options.BasicsGroup, out var basicsCap) && basicsTotal > basicsCap)
        {
            basicsScale = basicsCap / basicsTotal;
            contributions.Add((basicsCap - basicsTotal, $"Gruppe {_options.BasicsGroup} gedeckelt auf {basicsCap} ({Signed(basicsCap - basicsTotal)})"));
        }

        foreach (var (rule, points) in found)
        {
            if (rule.Rule.Group is { Length: > 0 } group && !(rule.IsBasic && string.Equals(group, _options.BasicsGroup, StringComparison.OrdinalIgnoreCase)))
            {
                groupTotals[group] = groupTotals.GetValueOrDefault(group) + (rule.IsBasic ? points * basicsScale : points);
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
            var (place, matched) = rule.Find(title, description);
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
                : $"{rule.Rule.Reason ?? rule.Rule.Name}{Matched(rule.Rule.Name, matched)}{Where(place)} ✗ ({Signed(points)})";
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

    /// <summary>The text found, when it is not just the rule's name: „Vitest („Unit Tests“)" makes an alias match visible.</summary>
    private static string Matched(string name, string? text) =>
        text is null || string.Equals(Normalize(text), Normalize(name), StringComparison.OrdinalIgnoreCase) ? string.Empty : $" („{text.Trim()}“)";

    private static string Normalize(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c != '-'));

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

    private sealed class CompiledRule(RelevanceRule rule, bool isBasic)
    {
        private readonly TermMatcher _matcher = new(rule.Terms, rule.MatchCase);

        public RelevanceRule Rule { get; } = rule;

        /// <summary>One of <see cref="RelevanceOptions.BasicSkills"/>.</summary>
        public bool IsBasic { get; } = isBasic;

        /// <summary>Every mention in the description lies in sections of this kind.</summary>
        public bool OnlyIn(string description, RequirementSections sections, SectionKind kind)
        {
            var hits = _matcher.FindAll(description).ToList();
            return hits.Count > 0 && hits.All(h => sections.At(h.Index) == kind);
        }

        public (Place Place, string? Text) Find(string title, string? description)
        {
            if (_matcher.FindFirstMatch(title) is { } inTitle)
            {
                return (Place.Title, inTitle.Text);
            }

            return !Rule.TitleOnly && _matcher.FindFirstMatch(description) is { } inText
                ? (Place.Description, inText.Text)
                : (Place.None, null);
        }
    }
}
