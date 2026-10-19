using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>
/// Runs searches against a source and upserts the results by <c>Source + ExternalId</c>:
/// new vacancies are added, known ones get fresh posting data but keep everything the user did with them
/// (<see cref="Vacancy.IsSeen"/>, <see cref="Vacancy.Interest"/>). Every vacancy remembers which searches found
/// it; a remote-only search makes it at least <see cref="RemoteLevel.Hybrid"/>, and the level is never lowered.
/// New and changed vacancies are scored right away (<see cref="VacancyEnricher"/>). Titles matching the exclusion
/// filter are not stored. Everything is saved at once at the end, so a failing source leaves the database untouched.
/// </summary>
public sealed class VacancyImportService(IVacancyStore store, TimeProvider timeProvider, VacancyEnricher? enricher = null)
{
    public async Task<ImportResult> ImportAsync(
        IVacancySource source,
        IReadOnlyList<VacancySearch> searches,
        TitleExclusionFilter exclusion,
        CancellationToken cancellationToken,
        Func<ImportProgress, CancellationToken, Task>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(searches);
        ArgumentNullException.ThrowIfNull(exclusion);

        var now = timeProvider.GetUtcNow();
        var tracked = new Dictionary<string, TrackedVacancy>(StringComparer.Ordinal);
        var excludedInRun = new HashSet<string>(StringComparer.Ordinal);
        var counts = new List<SearchCounts>(searches.Count);

        for (var index = 0; index < searches.Count; index++)
        {
            var search = searches[index];
            if (onProgress is not null)
            {
                await onProgress(new ImportProgress(index, searches.Count, search.Name), cancellationToken);
            }

            var searchCounts = new SearchCounts(search);
            counts.Add(searchCounts);
            var fresh = new Dictionary<string, VacancyCandidate>(StringComparer.Ordinal);

            await foreach (var candidate in source.SearchAsync(search, cancellationToken))
            {
                searchCounts.Fetched++;
                var id = candidate.ExternalId;

                if (tracked.TryGetValue(id, out var known))
                {
                    // Already handled by an earlier search of this run: only remember this search too.
                    if (known.Vacancy.MarkFoundBy(search.Name, search.RemoteOnly))
                    {
                        enricher?.Apply(known.Vacancy, candidate.RemoteHint);
                    }
                }
                else if (excludedInRun.Contains(id) || fresh.ContainsKey(id))
                {
                    // Duplicate of an excluded candidate, or repeated within this search.
                }
                else if (exclusion.IsExcluded(candidate.Title))
                {
                    excludedInRun.Add(id);
                    searchCounts.Excluded++;
                }
                else
                {
                    fresh.Add(id, candidate);
                }
            }

            if (fresh.Count == 0)
            {
                continue;
            }

            var existing = await store.FindByExternalIdsAsync(source.Source, [.. fresh.Keys], cancellationToken);
            foreach (var (id, candidate) in fresh)
            {
                TrackedVacancy entry;
                if (existing.TryGetValue(id, out var vacancy))
                {
                    entry = new TrackedVacancy(vacancy, index, Snapshot.Of(vacancy));
                    Refresh(vacancy, candidate);
                }
                else
                {
                    vacancy = Create(source.Source, candidate, now);
                    store.Add(vacancy);
                    entry = new TrackedVacancy(vacancy, index, Original: null);
                }

                vacancy.MarkFoundBy(search.Name, search.RemoteOnly);
                if (enricher is null)
                {
                    vacancy.RaiseRemoteLevel(candidate.RemoteHint);
                }
                else
                {
                    enricher.Apply(vacancy, candidate.RemoteHint);
                }

                tracked.Add(id, entry);
            }
        }

        // Last chance to stop: once saving starts, the run completes (one SaveChanges = one transaction).
        cancellationToken.ThrowIfCancellationRequested();
        await store.SaveChangesAsync(cancellationToken);

        // A vacancy is counted once, for the first search that found it; "updated" compares the final state
        // with the state before the run, so being found by a new search also counts as a change.
        foreach (var entry in tracked.Values)
        {
            var searchCounts = counts[entry.FirstSearchIndex];
            if (entry.Original is null)
            {
                searchCounts.Added++;
            }
            else if (entry.Original != Snapshot.Of(entry.Vacancy))
            {
                searchCounts.Updated++;
            }
            else
            {
                searchCounts.Unchanged++;
            }
        }

        var results = counts.ConvertAll(c => c.ToResult());
        return new ImportResult(
            results.Sum(r => r.Fetched),
            results.Sum(r => r.Added),
            results.Sum(r => r.Updated),
            results.Sum(r => r.Unchanged),
            results.Sum(r => r.Excluded),
            results);
    }

    private static Vacancy Create(VacancySource source, VacancyCandidate candidate, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Source = source,
        ExternalId = candidate.ExternalId,
        Title = candidate.Title,
        Company = candidate.Company,
        Location = candidate.Location,
        Url = candidate.Url,
        PublishedAt = candidate.PublishedAt,
        Description = candidate.Description,
        DetailsFetchedAt = candidate.Description is null ? null : now,
        EmployerType = EmployerType.Unknown,
        FetchedAt = now,
        IsSeen = false,
    };

    /// <summary>
    /// Copies posting data that differs. User state and <see cref="Vacancy.FetchedAt"/> stay as they are. Dates compare
    /// as instants, so a UTC value from the database equals the same moment in Berlin time from the source. A posting
    /// changed at the source after its details were fetched gets its details fetched again.
    /// </summary>
    private void Refresh(Vacancy vacancy, VacancyCandidate candidate)
    {
        if (candidate.Description is not null)
        {
            if (!string.Equals(vacancy.Description, candidate.Description, StringComparison.Ordinal))
            {
                vacancy.Description = candidate.Description;
            }

            vacancy.DetailsFetchedAt ??= timeProvider.GetUtcNow();
        }
        else if (candidate.ChangedAt is { } changedAt && vacancy.DetailsFetchedAt is { } detailsAt && changedAt > detailsAt)
        {
            vacancy.DetailsFetchedAt = null;
        }

        if (!string.Equals(vacancy.Title, candidate.Title, StringComparison.Ordinal))
        {
            vacancy.Title = candidate.Title;
        }

        if (!string.Equals(vacancy.Company, candidate.Company, StringComparison.Ordinal))
        {
            vacancy.Company = candidate.Company;
        }

        if (!string.Equals(vacancy.Location, candidate.Location, StringComparison.Ordinal))
        {
            vacancy.Location = candidate.Location;
        }

        if (!string.Equals(vacancy.Url, candidate.Url, StringComparison.Ordinal))
        {
            vacancy.Url = candidate.Url;
        }

        if (vacancy.PublishedAt != candidate.PublishedAt)
        {
            vacancy.PublishedAt = candidate.PublishedAt;
        }
    }

    /// <summary>The stored fields an import can change.</summary>
    private sealed record Snapshot(
        string Title,
        string Company,
        string? Location,
        string? Url,
        DateTimeOffset? PublishedAt,
        string? Description,
        RemoteLevel RemoteLevel,
        string MatchedSearches)
    {
        public static Snapshot Of(Vacancy v) => new(
            v.Title, v.Company, v.Location, v.Url, v.PublishedAt, v.Description, v.RemoteLevel,
            string.Join('\n', v.MatchedSearches));
    }

    private sealed record TrackedVacancy(Vacancy Vacancy, int FirstSearchIndex, Snapshot? Original);

    private sealed class SearchCounts(VacancySearch search)
    {
        public int Fetched { get; set; }

        public int Added { get; set; }

        public int Updated { get; set; }

        public int Unchanged { get; set; }

        public int Excluded { get; set; }

        public SearchImportResult ToResult() => new(search, Fetched, Added, Updated, Unchanged, Excluded);
    }
}
