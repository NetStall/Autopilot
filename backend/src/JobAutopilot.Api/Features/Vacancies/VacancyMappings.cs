using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Api.Features.Vacancies;

public static class VacancyMappings
{
    public static VacancyDto ToDto(
        this Vacancy vacancy,
        bool includeDescription = true,
        IReadOnlyList<VacancyLinkDto>? alsoFoundAt = null) => new(
        vacancy.Id,
        vacancy.Source,
        vacancy.ExternalId,
        vacancy.Title,
        vacancy.Company,
        vacancy.Location,
        vacancy.Url,
        includeDescription ? vacancy.Description : null,
        vacancy.EmployerType,
        vacancy.PublishedAt,
        vacancy.FetchedAt,
        vacancy.IsSeen,
        vacancy.RemoteLevel,
        [.. vacancy.MatchedSearches],
        vacancy.Interest,
        vacancy.RelevanceScore,
        vacancy.RankScore,
        vacancy.RelevanceLevel,
        [.. vacancy.RelevanceReasons],
        vacancy.DetailsFetchedAt,
        alsoFoundAt ?? []);

    public static Vacancy ToManualVacancy(this CreateVacancyRequest request, DateTimeOffset now) =>
        Vacancy.CreateManual(
            request.Title,
            request.Company,
            now,
            request.Location,
            request.Url,
            request.Description,
            request.EmployerType,
            request.PublishedAt);
}
