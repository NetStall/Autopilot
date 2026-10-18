using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>
/// Runs searches against a source and upserts the results by <c>Source + ExternalId</c>:
/// new vacancies are added, known ones get fresh posting data but keep everything the user did with them
/// (<see cref="Vacancy.IsSeen"/>, <see cref="Vacancy.EmployerType"/>). Everything is saved at once at the end,
/// so a failing source leaves the database untouched. Titles matching the exclusion filter are not stored.
/// </summary>
public sealed class VacancyImportService(IVacancyStore store, TimeProvider timeProvider)
{
    public async Task<ImportResult> ImportAsync(
        IVacancySource source,
        IReadOnlyList<VacancySearch> searches,
        TitleExclusionFilter exclusion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(searches);
        ArgumentNullException.ThrowIfNull(exclusion);

        var now = timeProvider.GetUtcNow();
        var handledInRun = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<SearchImportResult>(searches.Count);

        foreach (var search in searches)
        {
            var fetched = 0;
            var excluded = 0;
            var candidates = new List<VacancyCandidate>();
            await foreach (var candidate in source.SearchAsync(search, cancellationToken))
            {
                fetched++;
                if (!handledInRun.Add(candidate.ExternalId))
                {
                    continue;
                }

                if (exclusion.IsExcluded(candidate.Title))
                {
                    excluded++;
                }
                else
                {
                    candidates.Add(candidate);
                }
            }

            var existing = candidates.Count == 0
                ? new Dictionary<string, Vacancy>()
                : await store.FindByExternalIdsAsync(
                    source.Source, candidates.ConvertAll(c => c.ExternalId), cancellationToken);

            var added = 0;
            var updated = 0;
            var unchanged = 0;
            foreach (var candidate in candidates)
            {
                if (existing.TryGetValue(candidate.ExternalId, out var vacancy))
                {
                    if (Refresh(vacancy, candidate))
                    {
                        updated++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }
                else
                {
                    store.Add(Create(source.Source, candidate, now));
                    added++;
                }
            }

            results.Add(new SearchImportResult(search, fetched, added, updated, unchanged, excluded));
        }

        await store.SaveChangesAsync(cancellationToken);

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
        EmployerType = EmployerType.Unknown,
        FetchedAt = now,
        IsSeen = false,
    };

    /// <summary>
    /// Copies posting data and returns whether anything changed. User state and <see cref="Vacancy.FetchedAt"/>
    /// stay as they are. Dates compare as instants, so a UTC value from the database equals the same
    /// moment in Berlin time from the source.
    /// </summary>
    private static bool Refresh(Vacancy vacancy, VacancyCandidate candidate)
    {
        var changed = !string.Equals(vacancy.Title, candidate.Title, StringComparison.Ordinal)
            || !string.Equals(vacancy.Company, candidate.Company, StringComparison.Ordinal)
            || !string.Equals(vacancy.Location, candidate.Location, StringComparison.Ordinal)
            || !string.Equals(vacancy.Url, candidate.Url, StringComparison.Ordinal)
            || vacancy.PublishedAt != candidate.PublishedAt;

        if (changed)
        {
            vacancy.Title = candidate.Title;
            vacancy.Company = candidate.Company;
            vacancy.Location = candidate.Location;
            vacancy.Url = candidate.Url;
            vacancy.PublishedAt = candidate.PublishedAt;
        }

        return changed;
    }
}
