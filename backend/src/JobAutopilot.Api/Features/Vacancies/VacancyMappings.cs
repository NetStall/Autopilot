using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Api.Features.Vacancies;

public static class VacancyMappings
{
    public static VacancyDto ToDto(
        this Vacancy vacancy,
        bool includeDescription = true,
        IReadOnlyList<VacancyLinkDto>? alsoFoundAt = null,
        AppliedDto? applied = null,
        bool? goldLabel = null,
        Core.Profile.CandidateProfile? profile = null) => new(
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
        alsoFoundAt ?? [],
        applied,
        vacancy.RequiredGerman,
        vacancy.Unreachable,
        vacancy.HomeOfficePercent,
        vacancy.DescriptionIsManual,
        includeDescription && Core.Extraction.StoredExtraction.ForDescription(vacancy.ExtractionJson, vacancy.Description) is { } extraction
            ? new ExtractionDto(extraction.Result, extraction.Model, extraction.PromptVersion, extraction.ExtractedAt,
                Core.Extraction.ExtractionNormalizer.Chips(extraction.Result, profile))
            : null,
        goldLabel,
        vacancy.RemoteNote,
        CurrentAiScore(vacancy)?.Result.Score,
        includeDescription ? CurrentAiScore(vacancy)?.Result : null);

    /// <summary>The „KI-Bewertung" when it still belongs to the current description (the key starts with its hash).</summary>
    private static Core.Extraction.StoredFitScore? CurrentAiScore(Vacancy vacancy) =>
        vacancy.Description is { Length: > 0 } description && vacancy.AiScoreKey?.StartsWith(Core.Extraction.ExtractionKey.HashOf(description), StringComparison.Ordinal) == true
            ? Core.Extraction.StoredFitScore.Parse(vacancy.AiScoreJson)
            : null;

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
