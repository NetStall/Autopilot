using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>Limits for one details pass (configuration section <c>Import:Details</c>).</summary>
public sealed record VacancyDetailsOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>At most this many detail requests per import; the rest follow in the next runs.</summary>
    public int MaxPerRun { get; set; } = 300;

    /// <summary>Pause between two detail requests, to be polite to a public service.</summary>
    public int DelayBetweenRequestsMs { get; set; } = 300;

    /// <summary>Progress is saved after this many vacancies, so an interrupted pass loses little.</summary>
    public int SaveEvery { get; set; } = 20;
}

/// <summary>Counts of one details pass.</summary>
/// <param name="Stopped">The source became unavailable; the remaining vacancies are fetched next time.</param>
public sealed record DetailsResult(int Requested, int Fetched, int NotFound, bool Stopped);

/// <summary>
/// Fetches descriptions for vacancies that have none yet (new ones, and ones whose posting changed — the import
/// clears <see cref="Vacancy.DetailsFetchedAt"/> then). Requests run one after another with a pause; progress is saved
/// every few vacancies, so the pass can be stopped at any time and continues with the rest next time.
/// </summary>
public sealed class VacancyDetailsService(IVacancyStore store, VacancyEnricher enricher, TimeProvider timeProvider)
{
    public async Task<DetailsResult> FetchAsync(
        IVacancyDetailsSource source,
        VacancyDetailsOptions options,
        CancellationToken cancellationToken,
        Func<int, int, CancellationToken, Task>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var pending = await store.FindWithoutDetailsAsync(source.Source, options.MaxPerRun, cancellationToken);
        int fetched = 0, notFound = 0, sinceSave = 0;
        var stopped = false;

        try
        {
            for (var i = 0; i < pending.Count; i++)
            {
                if (onProgress is not null)
                {
                    await onProgress(i, pending.Count, cancellationToken);
                }

                if (i > 0 && options.DelayBetweenRequestsMs > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(options.DelayBetweenRequestsMs), timeProvider, cancellationToken);
                }

                var vacancy = pending[i];
                VacancyDetails? details;
                try
                {
                    details = await source.GetDetailsAsync(vacancy.ExternalId, cancellationToken);
                }
                catch (VacancySourceUnavailableException)
                {
                    stopped = true;
                    break;
                }

                if (details is null)
                {
                    notFound++;
                }
                else
                {
                    Apply(vacancy, details);
                    fetched++;
                }

                // Also for postings that are gone: otherwise they would be requested again in every run.
                vacancy.DetailsFetchedAt = timeProvider.GetUtcNow();

                if (++sinceSave >= options.SaveEvery)
                {
                    await store.SaveChangesAsync(cancellationToken);
                    sinceSave = 0;
                }
            }
        }
        finally
        {
            // Keep what was fetched, also when the pass was cancelled (shutdown) — every vacancy is complete on its own.
            await store.SaveChangesAsync(CancellationToken.None);
        }

        return new DetailsResult(pending.Count, fetched, notFound, stopped);
    }

    private void Apply(Vacancy vacancy, VacancyDetails details)
    {
        if (details.HomeOfficePercent is { } percent)
        {
            vacancy.HomeOfficePercent = percent;
        }

        if (!vacancy.DescriptionIsManual && !string.IsNullOrWhiteSpace(details.Description))
        {
            vacancy.Description = TextLimits.Truncate(details.Description.Trim(), VacancyLimits.DescriptionMaxLength);
        }

        enricher.Apply(vacancy, details.RemoteHint, details.EmployerHint);
    }
}
