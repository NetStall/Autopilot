using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Fakes;

/// <summary>Keeps vacancies in a list; additions only become visible after <see cref="SaveChangesAsync"/>.</summary>
public sealed class InMemoryVacancyStore : IVacancyStore
{
    private readonly List<Vacancy> _pending = [];

    public List<Vacancy> Saved { get; } = [];

    public int SaveCount { get; private set; }

    public Task<IReadOnlyDictionary<string, Vacancy>> FindByExternalIdsAsync(
        VacancySource source,
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, Vacancy> found = Saved
            .Where(v => v.Source == source && externalIds.Contains(v.ExternalId))
            .ToDictionary(v => v.ExternalId, StringComparer.Ordinal);
        return Task.FromResult(found);
    }

    public void Add(Vacancy vacancy) => _pending.Add(vacancy);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saved.AddRange(_pending);
        _pending.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }
}
