using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>
/// Runs searches against a source and upserts the results by <c>Source + ExternalId</c>:
/// new vacancies are added, known ones get fresh posting data but keep everything the user did with them
/// (<see cref="Vacancy.IsSeen"/>, <see cref="Vacancy.EmployerType"/>). Everything is saved at once at the end,
/// so a failing source leaves the database untouched.
/// </summary>
public sealed class VacancyImportService(IVacancyStore store, TimeProvider timeProvider)
{
    public async Task<ImportResult> ImportAsync(
        IVacancySource source,
        IReadOnlyList<VacancySearch> searches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(searches);

        var now = timeProvider.GetUtcNow();
        var handledInRun = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<SearchImportResult>(searches.Count);

        foreach (var search in searches)
        {
            var fetched = 0;
            var candidates = new List<VacancyCandidate>();
            await foreach (var candidate in source.SearchAsync(search, cancellationToken))
            {
                fetched++;
                if (handledInRun.Add(candidate.ExternalId))
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
            foreach (var candidate in candidates)
            {
                if (existing.TryGetValue(candidate.ExternalId, out var vacancy))
                {
                    Refresh(vacancy, candidate);
                    updated++;
                }
                else
                {
                    store.Add(Create(source.Source, candidate, now));
                    added++;
                }
            }

            results.Add(new SearchImportResult(search, fetched, added, updated));
        }

        await store.SaveChangesAsync(cancellationToken);

        return new ImportResult(
            results.Sum(r => r.Fetched),
            results.Sum(r => r.Added),
            results.Sum(r => r.Updated),
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

    /// <summary>Only posting data changes; user state and <see cref="Vacancy.FetchedAt"/> stay as they are.</summary>
    private static void Refresh(Vacancy vacancy, VacancyCandidate candidate)
    {
        vacancy.Title = candidate.Title;
        vacancy.Company = candidate.Company;
        vacancy.Location = candidate.Location;
        vacancy.Url = candidate.Url;
        vacancy.PublishedAt = candidate.PublishedAt;
    }
}
