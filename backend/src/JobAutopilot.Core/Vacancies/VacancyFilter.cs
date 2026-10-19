namespace JobAutopilot.Core.Vacancies;

/// <summary>
/// Criteria for browsing vacancies. <c>null</c> means "no restriction". <see cref="MatchedSearches"/> matches
/// vacancies found by any of the given searches. Dismissed vacancies are left out unless
/// <see cref="IncludeDismissed"/> is set or <see cref="Interests"/> asks for them explicitly. Vacancies that are not
/// scored yet count as <see cref="RelevanceLevel.Maybe"/>. <see cref="NotExpiredAt"/> hides postings expired at that
/// moment (they are deleted with the next import).
/// </summary>
public sealed record VacancyFilter(
    string? Text = null,
    string? Location = null,
    IReadOnlyCollection<EmployerType>? EmployerTypes = null,
    bool? IsSeen = null,
    VacancySource? Source = null,
    VacancySort Sort = VacancySort.Relevance,
    IReadOnlyCollection<RemoteLevel>? RemoteLevels = null,
    IReadOnlyCollection<string>? MatchedSearches = null,
    IReadOnlyCollection<VacancyInterest>? Interests = null,
    bool IncludeDismissed = false,
    DateTimeOffset? FetchedSince = null,
    IReadOnlyCollection<RelevanceLevel>? RelevanceLevels = null,
    DateTimeOffset? NotExpiredAt = null);

public enum VacancySort
{
    /// <summary>Highest relevance score first; at the same score direct employers before agencies, then newest.</summary>
    Relevance,

    /// <summary>Newest publication first; vacancies without a publication date last.</summary>
    Published,

    /// <summary>Most recently imported or created first.</summary>
    Fetched,
}
