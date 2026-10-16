using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>What the import needs from persistence. Changes are written only by <see cref="SaveChangesAsync"/>.</summary>
public interface IVacancyStore
{
    /// <summary>Returns the stored vacancies of <paramref name="source"/> with the given external ids, keyed by external id.
    /// Changes made to the returned instances are saved by <see cref="SaveChangesAsync"/>.</summary>
    Task<IReadOnlyDictionary<string, Vacancy>> FindByExternalIdsAsync(
        VacancySource source,
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken);

    void Add(Vacancy vacancy);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
