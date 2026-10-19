namespace JobAutopilot.Core.Vacancies;

/// <summary>Triage state set by the user, independent of seen/new.</summary>
public enum VacancyInterest
{
    None,

    /// <summary>„Gemerkt" — worth a closer look.</summary>
    Starred,

    /// <summary>„Ausgeblendet" — not interested; hidden from the default list.</summary>
    Dismissed,
}
