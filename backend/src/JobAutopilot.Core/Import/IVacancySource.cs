using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>A job board that can be searched. Implementations handle paging internally.</summary>
public interface IVacancySource
{
    VacancySource Source { get; }

    /// <exception cref="VacancySourceUnavailableException">The source could not be reached (after retries).</exception>
    IAsyncEnumerable<VacancyCandidate> SearchAsync(VacancySearch search, CancellationToken cancellationToken);
}
