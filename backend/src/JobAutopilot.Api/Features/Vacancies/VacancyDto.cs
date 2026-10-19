using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Api.Features.Vacancies;

public sealed record VacancyDto(
    Guid Id,
    VacancySource Source,
    string ExternalId,
    string Title,
    string Company,
    string? Location,
    string? Url,
    string? Description,
    EmployerType EmployerType,
    DateTimeOffset? PublishedAt,
    DateTimeOffset FetchedAt,
    bool IsSeen,
    RemoteLevel RemoteLevel,
    IReadOnlyList<string> MatchedSearches,
    VacancyInterest Interest,
    int? RelevanceScore,
    RelevanceLevel? RelevanceLevel,
    IReadOnlyList<string> RelevanceReasons,
    DateTimeOffset? DetailsFetchedAt);
