namespace JobAutopilot.Core.Import;

public enum ImportRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

public enum ImportTrigger
{
    Manual,
    Scheduled,
}

/// <summary>
/// One import, from being queued to its result. Progress fields are updated while it runs, so the UI can poll it.
/// </summary>
public class ImportRun
{
    public Guid Id { get; set; }

    public ImportTrigger Trigger { get; set; }

    public ImportRunStatus Status { get; set; }

    /// <summary>
    /// True while <see cref="ImportRunStatus.Queued"/> or <see cref="ImportRunStatus.Running"/>. A filtered unique
    /// index allows only one active run in the database — the guard against two imports at the same time.
    /// </summary>
    public bool IsActive { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int SearchesTotal { get; set; }

    public int SearchesDone { get; set; }

    /// <summary>Name of the search that is being fetched right now.</summary>
    public string? CurrentSearch { get; set; }

    public int Fetched { get; set; }

    public int Added { get; set; }

    public int Updated { get; set; }

    public int Unchanged { get; set; }

    public int Excluded { get; set; }

    /// <summary>User-facing reason when <see cref="ImportRunStatus.Failed"/>; never internals.</summary>
    public string? Error { get; set; }

    public List<ImportRunSearch> Searches { get; set; } = [];

    public static ImportRun Queue(ImportTrigger trigger, int searchesTotal, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Trigger = trigger,
        Status = ImportRunStatus.Queued,
        IsActive = true,
        RequestedAt = now,
        SearchesTotal = searchesTotal,
    };

    public void Start(DateTimeOffset now)
    {
        Status = ImportRunStatus.Running;
        StartedAt = now;
    }

    public void ReportProgress(ImportProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        SearchesDone = progress.SearchesDone;
        SearchesTotal = progress.SearchesTotal;
        CurrentSearch = progress.CurrentSearch;
    }

    public void Succeed(ImportResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);
        Fetched = result.Fetched;
        Added = result.Added;
        Updated = result.Updated;
        Unchanged = result.Unchanged;
        Excluded = result.Excluded;
        Searches = result.Searches.Select(ImportRunSearch.From).ToList();
        SearchesDone = SearchesTotal;
        Finish(ImportRunStatus.Succeeded, now, error: null);
    }

    public void Fail(string error, DateTimeOffset now) => Finish(ImportRunStatus.Failed, now, error);

    public void Cancel(DateTimeOffset now) => Finish(ImportRunStatus.Cancelled, now, "Import was cancelled (the API was shutting down).");

    private void Finish(ImportRunStatus status, DateTimeOffset now, string? error)
    {
        Status = status;
        IsActive = false;
        FinishedAt = now;
        CurrentSearch = null;
        Error = error;
    }
}

/// <summary>Per-search counts of a finished run (stored as JSON).</summary>
public class ImportRunSearch
{
    public required string Name { get; set; }

    public required string Keyword { get; set; }

    public string? Location { get; set; }

    public bool RemoteOnly { get; set; }

    public int Fetched { get; set; }

    public int Added { get; set; }

    public int Updated { get; set; }

    public int Unchanged { get; set; }

    public int Excluded { get; set; }

    public static ImportRunSearch From(SearchImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ImportRunSearch
        {
            Name = result.Search.Name,
            Keyword = result.Search.Keyword,
            Location = result.Search.Location,
            RemoteOnly = result.Search.RemoteOnly,
            Fetched = result.Fetched,
            Added = result.Added,
            Updated = result.Updated,
            Unchanged = result.Unchanged,
            Excluded = result.Excluded,
        };
    }
}

/// <summary>Reported before each search and once at the end.</summary>
public sealed record ImportProgress(int SearchesDone, int SearchesTotal, string? CurrentSearch);
