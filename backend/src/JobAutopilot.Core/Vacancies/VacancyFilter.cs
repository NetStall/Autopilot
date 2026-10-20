namespace JobAutopilot.Core.Vacancies;

/// <summary>
/// Criteria for browsing vacancies. <c>null</c> means "no restriction". <see cref="MatchedSearches"/> matches
/// vacancies found by any of the given searches. Dismissed vacancies are left out unless
/// <see cref="IncludeDismissed"/> is set or <see cref="Interests"/> asks for them explicitly. Vacancies that are not
/// scored yet count as <see cref="RelevanceLevel.Maybe"/>. <see cref="NotExpiredAt"/> hides postings expired at that
/// moment (they are deleted with the next import). <see cref="MinScore"/> compares the rank score (relevance minus fit
/// penalties); unscored vacancies are left out by any minimum above 0. <see cref="HiddenEmployerTypes"/> (Suchprofil
/// „ausblenden") are left out unless <see cref="EmployerTypes"/> asks for them. <see cref="RequiredGerman"/> filters by
/// the German the posting asks for („Deutsch erforderlich"). <see cref="HideUnreachable"/> leaves out vacancies marked
/// as not reachable (Suchprofil „Nicht erreichbare Stellen ausblenden").
/// </summary>
public sealed record VacancyFilter(
    string? Text = null,
    string? Location = null,
    IReadOnlyCollection<EmployerType>? EmployerTypes = null,
    bool? IsSeen = null,
    IReadOnlyCollection<VacancySource>? Sources = null,
    VacancySort Sort = VacancySort.Relevance,
    IReadOnlyCollection<RemoteLevel>? RemoteLevels = null,
    IReadOnlyCollection<string>? MatchedSearches = null,
    IReadOnlyCollection<VacancyInterest>? Interests = null,
    bool IncludeDismissed = false,
    DateTimeOffset? FetchedSince = null,
    IReadOnlyCollection<RelevanceLevel>? RelevanceLevels = null,
    DateTimeOffset? NotExpiredAt = null,
    int? MinScore = null,
    IReadOnlyCollection<EmployerType>? HiddenEmployerTypes = null,
    IReadOnlyCollection<RequiredGerman>? RequiredGerman = null,
    bool HideUnreachable = false);

public enum VacancySort
{
    /// <summary>Highest relevance score first; at the same score direct employers before agencies, then newest.</summary>
    Relevance,

    /// <summary>Newest publication first; vacancies without a publication date last.</summary>
    Published,

    /// <summary>Most recently imported or created first.</summary>
    Fetched,
}
