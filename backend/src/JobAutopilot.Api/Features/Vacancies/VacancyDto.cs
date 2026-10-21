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
    int? RankScore,
    RelevanceLevel? RelevanceLevel,
    IReadOnlyList<string> RelevanceReasons,
    DateTimeOffset? DetailsFetchedAt,
    IReadOnlyList<VacancyLinkDto> AlsoFoundAt,
    AppliedDto? Applied,
    RequiredGerman RequiredGerman = RequiredGerman.Unknown,
    bool Unreachable = false,
    int? HomeOfficePercent = null,
    bool DescriptionIsManual = false,
    ExtractionDto? Extraction = null,
    bool? GoldLabel = null);

/// <summary>The model's extraction for the current description (detail view only), step 07a.</summary>
public sealed record ExtractionDto(Core.Extraction.RequirementExtraction Result, string Model, string PromptVersion, DateTimeOffset ExtractedAt);

/// <summary>I already applied — to this vacancy, or to the same company with a similar title („Beworben").</summary>
public sealed record AppliedDto(Guid ApplicationId, Core.Applications.ApplicationStatus Status, string StatusLabel, DateTimeOffset AppliedAt);

/// <summary>The same job in another place (other source or a repost), see <c>DuplicateKey</c>.</summary>
public sealed record VacancyLinkDto(Guid Id, VacancySource Source, string? Url);
