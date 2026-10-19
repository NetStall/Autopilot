using JobAutopilot.Core.Import;

namespace JobAutopilot.Core.Scoring;

/// <summary>Applies the current scoring configuration to every stored vacancy, page by page.</summary>
public sealed class VacancyRescoreService(IVacancyStore store, VacancyEnricher enricher)
{
    public const int PageSize = 200;

    /// <returns>The number of vacancies scored.</returns>
    public async Task<int> RescoreAllAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var page = await store.ListPageAsync(total, PageSize, cancellationToken);
            foreach (var vacancy in page)
            {
                enricher.Apply(vacancy);
            }

            await store.SaveChangesAsync(cancellationToken);
            total += page.Count;
            if (page.Count < PageSize)
            {
                return total;
            }
        }
    }
}
