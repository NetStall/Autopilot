namespace JobAutopilot.Core.Vacancies;

/// <summary>How well a vacancy matches the profile, from <see cref="Scoring.RelevanceScorer"/>.</summary>
public enum RelevanceLevel
{
    Irrelevant,
    Maybe,
    Relevant,
}
