using JobAutopilot.Core.Import;

namespace JobAutopilot.Api.Features.VacancyImports;

public sealed record ImportResultDto(
    int Fetched,
    int Added,
    int Updated,
    int Unchanged,
    int Excluded,
    long DurationMs,
    IReadOnlyList<SearchImportResultDto> Searches);

public sealed record SearchImportResultDto(
    string Name,
    string Keyword,
    string? Location,
    bool RemoteOnly,
    int Fetched,
    int Added,
    int Updated,
    int Unchanged,
    int Excluded)
{
    public static SearchImportResultDto From(SearchImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(
            result.Search.Name,
            result.Search.Keyword,
            result.Search.Location,
            result.Search.RemoteOnly,
            result.Fetched,
            result.Added,
            result.Updated,
            result.Unchanged,
            result.Excluded);
    }
}

/// <summary>A configured search as shown in the UI.</summary>
public sealed record ConfiguredSearchDto(
    string Name,
    string Keyword,
    string? Location,
    int? RadiusKm,
    int PublishedWithinDays,
    bool RemoteOnly)
{
    public static ConfiguredSearchDto From(VacancySearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        return new(search.Name, search.Keyword, search.Location, search.RadiusKm, search.PublishedWithinDays, search.RemoteOnly);
    }
}
