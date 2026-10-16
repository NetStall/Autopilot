using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Infrastructure.Persistence;

internal sealed class EfVacancyStore(AppDbContext db) : IVacancyStore
{
    public async Task<IReadOnlyDictionary<string, Vacancy>> FindByExternalIdsAsync(
        VacancySource source,
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken)
    {
        // Tracked on purpose: the import changes them and SaveChangesAsync writes the changes.
        return await db.Vacancies
            .Where(v => v.Source == source && externalIds.Contains(v.ExternalId))
            .ToDictionaryAsync(v => v.ExternalId, StringComparer.Ordinal, cancellationToken);
    }

    public void Add(Vacancy vacancy) => db.Vacancies.Add(vacancy);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
