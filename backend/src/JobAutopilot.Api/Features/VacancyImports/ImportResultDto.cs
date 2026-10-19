using JobAutopilot.Core.Import;

namespace JobAutopilot.Api.Features.VacancyImports;

/// <summary>Response of <c>POST /api/imports</c> (202 Accepted).</summary>
public sealed record ImportStartedDto(Guid ImportId);

/// <summary>An import run: status, progress while running, counts when finished.</summary>
public sealed record ImportRunDto(
    Guid Id,
    ImportTrigger Trigger,
    ImportRunStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long? DurationMs,
    int SearchesTotal,
    int SearchesDone,
    string? CurrentSearch,
    int Fetched,
    int Added,
    int Updated,
    int Unchanged,
    int Excluded,
    string? Error,
    IReadOnlyList<SearchImportResultDto> Searches)
{
    public static ImportRunDto From(ImportRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        long? duration = run is { StartedAt: { } started, FinishedAt: { } finished }
            ? (long)(finished - started).TotalMilliseconds
            : null;
        return new(
            run.Id,
            run.Trigger,
            run.Status,
            run.RequestedAt,
            run.StartedAt,
            run.FinishedAt,
            duration,
            run.SearchesTotal,
            run.SearchesDone,
            run.CurrentSearch,
            run.Fetched,
            run.Added,
            run.Updated,
            run.Unchanged,
            run.Excluded,
            run.Error,
            [.. run.Searches.Select(SearchImportResultDto.From)]);
    }
}

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
    public static SearchImportResultDto From(ImportRunSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        return new(
            search.Name,
            search.Keyword,
            search.Location,
            search.RemoteOnly,
            search.Fetched,
            search.Added,
            search.Updated,
            search.Unchanged,
            search.Excluded);
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
