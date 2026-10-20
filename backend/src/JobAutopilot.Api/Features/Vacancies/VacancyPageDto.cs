namespace JobAutopilot.Api.Features.Vacancies;

/// <summary>
/// Response of <c>GET /api/vacancies</c>: a page plus <see cref="TotalMatching"/> (before the „Top N" cut, for
/// „Top 20 von 258") and the facet counts.
/// </summary>
public sealed record VacancyPageDto(
    IReadOnlyList<VacancyDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalMatching,
    int? Top,
    VacancyFacetsDto Facets);

/// <summary>Counts per value of each filter, under all <em>other</em> active filters.</summary>
public sealed record VacancyFacetsDto(
    IReadOnlyDictionary<string, int> Source,
    IReadOnlyDictionary<string, int> RemoteLevel,
    IReadOnlyDictionary<string, int> EmployerType,
    IReadOnlyDictionary<string, int> RelevanceLevel,
    IReadOnlyDictionary<string, int> RequiredGerman);
