using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Extraction;

/// <summary>A vacancy's label for the evaluation: mine, or derived from what I did with it (<see cref="Automatic"/>).</summary>
public sealed record VacancyLabel(Guid VacancyId, bool Fits, bool Automatic);

/// <summary>
/// Labels from what I already did (step 07c), so the evaluation does not wait for labelling one by one: applied to it
/// („Gesendet" or any status after it) → „Passt"; „Ausblenden" → „Passt nicht". An explicit label always wins.
/// </summary>
public static class ImplicitLabels
{
    /// <summary>Sent or later — a draft or an approved draft is not an application yet.</summary>
    public static bool Applied(ApplicationStatus status) => status is not (ApplicationStatus.Draft or ApplicationStatus.Approved);

    public static bool? For(bool applied, VacancyInterest interest) =>
        applied ? true : interest == VacancyInterest.Dismissed ? false : null;

    /// <summary>The explicit label when there is one, otherwise the implicit one.</summary>
    public static VacancyLabel? Combine(Guid vacancyId, bool? explicitLabel, bool? implicitLabel) =>
        explicitLabel is { } fits ? new VacancyLabel(vacancyId, fits, false)
        : implicitLabel is { } derived ? new VacancyLabel(vacancyId, derived, true)
        : null;
}
