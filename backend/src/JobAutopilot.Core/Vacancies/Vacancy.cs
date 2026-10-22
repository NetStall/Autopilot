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

    /// <summary>When the source takes the posting down (Himalayas); expired vacancies are deleted.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Company + title normalised (<see cref="DuplicateKeys"/>): the same job from several sources.</summary>
    public string? DuplicateKey { get; set; }

    /// <summary>When the vacancy entered this system.</summary>
    public DateTimeOffset FetchedAt { get; set; }

    public bool IsSeen { get; set; }

    /// <summary>„Merken" / „Ausblenden" — user triage, never touched by imports.</summary>
    public VacancyInterest Interest { get; set; }

    /// <summary>
    /// How remote the job is, recomputed on every scoring from the current rules: an explicit phrase in title or
    /// description wins („anteilig Homeoffice" → hybrid even if the source says 100 %), otherwise
    /// <see cref="RemoteHint"/>.
    /// </summary>
    public RemoteLevel RemoteLevel { get; set; }

    /// <summary>
    /// What the source and the searches said: the source's home-office data (Arbeitsagentur
    /// <c>homeofficeprozent</c>, Himalayas = remote) and „found by a remote-only search" (→ at least
    /// <see cref="RemoteLevel.Hybrid"/>). The highest value seen so far — this signal is sticky.
    /// </summary>
    public RemoteLevel RemoteHint { get; set; }

    /// <summary>When the description and details were last fetched from the source; <c>null</c> = still to fetch.</summary>
    public DateTimeOffset? DetailsFetchedAt { get; set; }

    /// <summary>0–100 from the relevance scorer; <c>null</c> until scored.</summary>
    public int? RelevanceScore { get; set; }

    public RelevanceLevel? RelevanceLevel { get; set; }

    /// <summary>
    /// The ranking value of the default list: <see cref="RelevanceScore"/> minus the fit penalties (German level above
    /// mine, more years than I have, required gaps such as Angular, an employer type I avoid). <c>null</c> until scored.
    /// </summary>
    public int? RankScore { get; set; }

    /// <summary>
    /// The same rank without any AI input (no extraction, remote level from the rules): sort „Nur Regeln" and the
    /// evaluation's baseline (07c). Equals <see cref="RankScore"/> while there is no extraction.
    /// </summary>
    public int? RuleRankScore { get; set; }

    /// <summary>Layer 1 (08b): why the vacancy is hidden by default (<see cref="Filters.HardFilters"/>); <c>null</c> = shown.</summary>
    public Filters.HardFilterReason? FilterReason { get; set; }

    /// <summary>„Deutsch C1 („verhandlungssicher“)", „Rolle: „Werkstudent“".</summary>
    public string? FilterNote { get; set; }

    /// <summary>The German the posting asks for (filter „Deutsch erforderlich"); set on every (re-)scoring.</summary>
    public RequiredGerman RequiredGerman { get; set; }

    /// <summary>
    /// Practically not reachable: outside my places, not fully remote, at most 60 % home office or an unknown share
    /// (Suchprofil „Nicht erreichbare Stellen ausblenden"); set on every (re-)scoring.
    /// </summary>
    public bool Unreachable { get; set; }

    /// <summary>The home-office share stated by the source as a field (Arbeitsagentur <c>homeofficeprozent</c>), if any.</summary>
    public int? HomeOfficePercent { get; set; }

    /// <summary>Home office is clearly partial („teilweise im Homeoffice", a share below 100 %, on site) — 07b.</summary>
    public bool RemotePartial { get; set; }

    /// <summary>What the remote badge says and where it comes from, e.g. „Homeoffice: bis 40 % (Feld)" (German).</summary>
    public string? RemoteNote { get; set; }

    /// <summary>The description was pasted by hand („Beschreibung einfügen"); fetching details never overwrites it.</summary>
    public bool DescriptionIsManual { get; set; }

    /// <summary>
    /// The model's extraction for the current description (<see cref="Extraction.StoredExtraction"/> as JSON, step 07a);
    /// ignored as soon as the description changes (its hash no longer matches).
    /// </summary>
    public string? ExtractionJson { get; set; }

    /// <summary><see cref="Extraction.ExtractionKey"/> of <see cref="ExtractionJson"/> — finds vacancies still to analyse.</summary>
    public string? ExtractionKey { get; set; }

    /// <summary>„KI-Bewertung" 0–100 (step 07b): the local model's own judgement; shown next to the rank, not part of it.</summary>
    public int? AiScore { get; set; }

    /// <summary>Description hash + profile hash + model + prompt of <see cref="AiScoreJson"/>.</summary>
    public string? AiScoreKey { get; set; }

    /// <summary>The <see cref="Extraction.StoredFitScore"/> (verdict, reasons, blockers) as JSON.</summary>
    public string? AiScoreJson { get; set; }

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
            changed |= RaiseRemoteHint(RemoteLevel.Hybrid);
        }

        return changed;
    }

    /// <summary>Sets <see cref="RemoteHint"/> to <paramref name="level"/> if that is higher; returns whether it changed.</summary>
    public bool RaiseRemoteHint(RemoteLevel level)
    {
        if (level <= RemoteHint)
        {
            return false;
        }

        RemoteHint = level;
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
