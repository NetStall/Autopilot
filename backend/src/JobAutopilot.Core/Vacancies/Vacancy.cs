namespace JobAutopilot.Core.Vacancies;

/// <summary>A job posting from a source. <see cref="Source"/> + <see cref="ExternalId"/> identify it uniquely.</summary>
public class Vacancy
{
    public Guid Id { get; set; }

    public VacancySource Source { get; set; }

    /// <summary>The id of the posting in its source (e.g. the Arbeitsagentur reference number).</summary>
    public required string ExternalId { get; set; }

    public required string Title { get; set; }

    public required string Company { get; set; }

    public string? Location { get; set; }

    public string? Url { get; set; }

    public string? Description { get; set; }

    public EmployerType EmployerType { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>When the vacancy entered this system.</summary>
    public DateTimeOffset FetchedAt { get; set; }

    public bool IsSeen { get; set; }

    /// <summary>„Merken" / „Ausblenden" — user triage, never touched by imports.</summary>
    public VacancyInterest Interest { get; set; }

    /// <summary>
    /// How remote the job is: the highest level any signal gave so far (remote-only search → at least
    /// <see cref="RemoteLevel.Hybrid"/>, the source's home-office data, phrases in title/description). Never lowered.
    /// </summary>
    public RemoteLevel RemoteLevel { get; set; }

    /// <summary>When the description and details were last fetched from the source; <c>null</c> = still to fetch.</summary>
    public DateTimeOffset? DetailsFetchedAt { get; set; }

    /// <summary>0–100 from the relevance scorer; <c>null</c> until scored.</summary>
    public int? RelevanceScore { get; set; }

    public RelevanceLevel? RelevanceLevel { get; set; }

    /// <summary>Why the vacancy got its score, remote level and employer type (German, shown in the UI).</summary>
    public List<string> RelevanceReasons { get; set; } = [];

    /// <summary>Names of all searches that have found this vacancy (no duplicates). Empty for manual vacancies.</summary>
    public List<string> MatchedSearches { get; set; } = [];

    /// <summary>Records that <paramref name="searchName"/> found this vacancy; returns whether anything changed.</summary>
    public bool MarkFoundBy(string searchName, bool remoteOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchName);
        var changed = false;

        if (!MatchedSearches.Contains(searchName, StringComparer.OrdinalIgnoreCase))
        {
            MatchedSearches.Add(searchName);
            changed = true;
        }

        if (remoteOnly)
        {
            changed |= RaiseRemoteLevel(RemoteLevel.Hybrid);
        }

        return changed;
    }

    /// <summary>Sets <see cref="RemoteLevel"/> to <paramref name="level"/> if that is higher; returns whether it changed.</summary>
    public bool RaiseRemoteLevel(RemoteLevel level)
    {
        if (level <= RemoteLevel)
        {
            return false;
        }

        RemoteLevel = level;
        return true;
    }

    /// <summary>
    /// Creates a vacancy that Vitalii entered by hand. Manual vacancies have no id in any
    /// external system, so a unique one is generated.
    /// </summary>
    public static Vacancy CreateManual(
        string title,
        string company,
        DateTimeOffset now,
        string? location = null,
        string? url = null,
        string? description = null,
        EmployerType employerType = EmployerType.Unknown,
        DateTimeOffset? publishedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(company);

        var id = Guid.CreateVersion7(now);
        return new Vacancy
        {
            Id = id,
            Source = VacancySource.Manual,
            ExternalId = $"manual-{id:N}",
            Title = title.Trim(),
            Company = company.Trim(),
            Location = NullIfBlank(location),
            Url = NullIfBlank(url),
            Description = NullIfBlank(description),
            EmployerType = employerType,
            PublishedAt = publishedAt,
            FetchedAt = now,
            IsSeen = false,
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
