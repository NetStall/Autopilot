using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Api.Features.Vacancies;

public static class VacancyMappings
{
    public static VacancyDto ToDto(this Vacancy vacancy) => new(
        vacancy.Id,
        vacancy.Source,
        vacancy.ExternalId,
        vacancy.Title,
        vacancy.Company,
        vacancy.Location,
        vacancy.Url,
        vacancy.Description,
        vacancy.EmployerType,
        vacancy.PublishedAt,
        vacancy.FetchedAt,
        vacancy.IsSeen,
        vacancy.IsRemote,
        [.. vacancy.MatchedSearches]);

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
