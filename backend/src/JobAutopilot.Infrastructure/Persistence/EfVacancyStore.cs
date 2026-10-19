using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Infrastructure.Persistence;

// Everything returned is tracked on purpose: the callers change the vacancies and SaveChangesAsync writes the changes.
internal sealed class EfVacancyStore(AppDbContext db) : IVacancyStore
{
    public async Task<IReadOnlyDictionary<string, Vacancy>> FindByExternalIdsAsync(
        VacancySource source,
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken)
    {
        return await db.Vacancies
            .Where(v => v.Source == source && externalIds.Contains(v.ExternalId))
            .ToDictionaryAsync(v => v.ExternalId, StringComparer.Ordinal, cancellationToken);
    }

    public async Task<IReadOnlyList<Vacancy>> FindWithoutDetailsAsync(
        VacancySource source,
        int max,
        CancellationToken cancellationToken)
    {
        return await db.Vacancies
            .Where(v => v.Source == source && v.DetailsFetchedAt == null)
            .OrderByDescending(v => v.FetchedAt)
            .ThenByDescending(v => v.Id)
            .Take(max)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Vacancy>> ListPageAsync(int skip, int take, CancellationToken cancellationToken)
    {
        return await db.Vacancies
            .OrderBy(v => v.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var utc = now.ToUniversalTime();
        return db.Vacancies.Where(v => v.ExpiresAt != null && v.ExpiresAt < utc).ExecuteDeleteAsync(cancellationToken);
    }

    public void Add(Vacancy vacancy) => db.Vacancies.Add(vacancy);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
