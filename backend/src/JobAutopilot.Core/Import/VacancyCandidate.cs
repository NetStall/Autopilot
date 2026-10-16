namespace JobAutopilot.Core.Import;

/// <summary>A search result from any source, already mapped to source-neutral fields.</summary>
public sealed record VacancyCandidate(
    string ExternalId,
    string Title,
    string Company,
    string? Location,
    string? Url,
    DateTimeOffset? PublishedAt);
