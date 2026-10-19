using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>
/// What import, details fetching and re-scoring need from persistence. Returned vacancies are tracked: changes to
/// them are written by <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IVacancyStore
{
    /// <summary>Returns the stored vacancies of <paramref name="source"/> with the given external ids, keyed by external id.</summary>
    Task<IReadOnlyDictionary<string, Vacancy>> FindByExternalIdsAsync(
        VacancySource source,
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="max"/> vacancies of <paramref name="source"/> without details, newest first.</summary>
    Task<IReadOnlyList<Vacancy>> FindWithoutDetailsAsync(VacancySource source, int max, CancellationToken cancellationToken);

    /// <summary>One page of all vacancies in a stable order (for re-scoring everything).</summary>
    Task<IReadOnlyList<Vacancy>> ListPageAsync(int skip, int take, CancellationToken cancellationToken);

    void Add(Vacancy vacancy);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
