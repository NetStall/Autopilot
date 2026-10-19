using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>A search result from any source, already mapped to source-neutral fields.</summary>
/// <param name="Description">Plain-text description when the search result already contains it (otherwise the
/// details are fetched later, see <see cref="IVacancyDetailsSource"/>).</param>
/// <param name="RemoteHint">What the source itself says about home office (e.g. Arbeitsagentur
/// <c>homeofficeprozent</c>).</param>
/// <param name="ChangedAt">When the posting was last changed at the source; a newer value refetches the details.</param>
public sealed record VacancyCandidate(
    string ExternalId,
    string Title,
    string Company,
    string? Location,
    string? Url,
    DateTimeOffset? PublishedAt,
    string? Description = null,
    RemoteLevel RemoteHint = RemoteLevel.Unknown,
    DateTimeOffset? ChangedAt = null);
