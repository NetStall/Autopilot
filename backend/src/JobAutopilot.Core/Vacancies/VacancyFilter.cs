namespace JobAutopilot.Core.Vacancies;

/// <summary>Criteria for browsing vacancies. <c>null</c> means "no restriction".</summary>
public sealed record VacancyFilter(
    string? Text = null,
    string? Location = null,
    IReadOnlyCollection<EmployerType>? EmployerTypes = null,
    bool? IsSeen = null,
    VacancySource? Source = null,
    VacancySort Sort = VacancySort.Published);

public enum VacancySort
{
    /// <summary>Newest publication first; vacancies without a publication date last.</summary>
    Published,

    /// <summary>Most recently imported or created first.</summary>
    Fetched,
}
