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

    public Task<IReadOnlyList<Vacancy>> FindWithoutDetailsAsync(VacancySource source, int max, CancellationToken cancellationToken)
    {
        IReadOnlyList<Vacancy> found = Saved
            .Where(v => v.Source == source && v.DetailsFetchedAt is null)
            .OrderByDescending(v => v.RankScore ?? v.RelevanceScore ?? 0)
            .ThenByDescending(v => v.FetchedAt)
            .Take(max)
            .ToList();
        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<Vacancy>> ListPageAsync(int skip, int take, CancellationToken cancellationToken)
    {
        IReadOnlyList<Vacancy> page = Saved.OrderBy(v => v.Id).Skip(skip).Take(take).ToList();
        return Task.FromResult(page);
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.RemoveAll(v => v.ExpiresAt < now));

    public void Add(Vacancy vacancy) => _pending.Add(vacancy);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saved.AddRange(_pending);
        _pending.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }
}
