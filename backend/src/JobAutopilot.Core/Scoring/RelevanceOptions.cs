namespace JobAutopilot.Core.Scoring;

/// <summary>
/// Configuration section <c>Relevance</c>: keyword weights and thresholds of <see cref="RelevanceScorer"/>.
/// The score starts at <see cref="BaseScore"/>; every rule found adds (positive) or subtracts (negative) its weight,
/// multiplied by <see cref="TitleFactor"/> when it is in the title. The result is clamped to 0–100.
/// </summary>
public sealed class RelevanceOptions
{
    public const string SectionName = "Relevance";

    public int BaseScore { get; set; } = 20;

    public double TitleFactor { get; set; } = 2;

    /// <summary>
    /// Weight factor for a negative rule with <see cref="RelevanceRule.SoftenedByCore"/> when a core rule (C#, .NET)
    /// is found at least as prominently — e.g. „Java oder C#" in the title, or „Python von Vorteil" in the text of a
    /// .NET job.
    /// </summary>
    public double SoftenedFactor { get; set; } = 0.25;

    /// <summary>Scores from here on are <c>Relevant</c>.</summary>
    public int RelevantFrom { get; set; } = 55;

    /// <summary>Scores from here on (below <see cref="RelevantFrom"/>) are <c>Maybe</c>; below it <c>Irrelevant</c>.</summary>
    public int MaybeFrom { get; set; } = 30;

    public List<RelevanceRule> Positive { get; init; } = [];

    /// <summary>Maximum points per <see cref="RelevanceRule.Group"/>, e.g. <c>dotnet: 50</c>; groups without a cap are not limited.</summary>
    public Dictionary<string, int> GroupCaps { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<RelevanceRule> Negative { get; init; } = [];

    /// <summary>How the candidate profile's skills count as positive keywords.</summary>
    public ProfileSkillWeights ProfileSkills { get; init; } = new();

    /// <summary>Re-score all vacancies when the API starts (picks up changed weights and phrases).</summary>
    public bool RescoreOnStartup { get; set; } = true;

    /// <summary>Human-readable problems with the configuration; empty when it is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (BaseScore is < 0 or > 100)
        {
            errors.Add("BaseScore must be between 0 and 100.");
        }

        if (TitleFactor is < 1 or > 10)
        {
            errors.Add("TitleFactor must be between 1 and 10.");
        }

        if (SoftenedFactor is < 0 or > 1)
        {
            errors.Add("SoftenedFactor must be between 0 and 1.");
        }

        if (MaybeFrom < 0 || RelevantFrom > 100 || MaybeFrom >= RelevantFrom)
        {
            errors.Add("Thresholds must satisfy 0 <= MaybeFrom < RelevantFrom <= 100.");
        }

        errors.AddRange(GroupCaps.Where(c => c.Value is < 1 or > 100).Select(c => $"GroupCaps '{c.Key}' must be between 1 and 100."));
        errors.AddRange(ValidateRules(Positive, nameof(Positive)));
        errors.AddRange(ValidateRules(Negative, nameof(Negative)));
        return errors;
    }

    private static IEnumerable<string> ValidateRules(List<RelevanceRule> rules, string kind)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var label = string.IsNullOrWhiteSpace(rule.Name) ? $"#{i + 1}" : $"'{rule.Name}'";
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                yield return $"{kind} rule {label}: Name is required.";
            }

            if (rule.Terms.Count == 0 || rule.Terms.Any(string.IsNullOrWhiteSpace))
            {
                yield return $"{kind} rule {label}: Terms must contain at least one non-empty term.";
            }

            if (rule.Weight is < 1 or > 100)
            {
                yield return $"{kind} rule {label}: Weight must be between 1 and 100.";
            }
        }
    }
}

/// <summary>Weights for skills from the profile by level; <see cref="Group"/> is used when a skill names none.</summary>
public sealed class ProfileSkillWeights
{
    public int Production { get; set; } = 4;

    public int Project { get; set; } = 2;

    public int Learning { get; set; } = 1;

    public string Group { get; set; } = "profile";
}

/// <summary>One keyword group, e.g. Name „C#" with Terms „C#", „CSharp".</summary>
public sealed class RelevanceRule
{
    /// <summary>Shown in the reasons, e.g. „C# im Titel ✓ (+50)".</summary>
    public string Name { get; set; } = string.Empty;

    public List<string> Terms { get; init; } = [];

    public int Weight { get; set; }

    /// <summary>Case-sensitive terms, e.g. „REST" (not „the rest of"), „SAP".</summary>
    public bool MatchCase { get; set; }

    /// <summary>Only the title counts — e.g. „Ausbildung" in a description is usually a requirement.</summary>
    public bool TitleOnly { get; set; }

    /// <summary>Positive rules: the core stack; softens negative rules marked <see cref="SoftenedByCore"/>.</summary>
    public bool Core { get; set; }

    /// <summary>Positive rules: skills of one group share the group's cap (<see cref="RelevanceOptions.GroupCaps"/>).</summary>
    public string? Group { get; set; }

    /// <summary>Negative rules: an alternative technology that may appear next to the core stack (Java, Python …).</summary>
    public bool SoftenedByCore { get; set; }

    /// <summary>Text for the reason when the rule counts fully, e.g. „Java als Hauptsprache"; default <see cref="Name"/>.</summary>
    public string? Reason { get; set; }
}
