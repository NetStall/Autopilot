namespace JobAutopilot.Core.Vacancies;

/// <summary>Where a vacancy came from.</summary>
public enum VacancySource
{
    Arbeitsagentur,
    Manual,

    /// <summary>Himalayas remote jobs API (ADR 0008).</summary>
    Himalayas,
}
