using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Import;

/// <summary>Full details of one posting: the description and what the source says about employer and home office.</summary>
/// <param name="EmployerHint">From the source's own flags, e.g. Arbeitsagentur <c>istArbeitnehmerUeberlassung</c>.</param>
public sealed record VacancyDetails(
    string? Description,
    RemoteLevel RemoteHint = RemoteLevel.Unknown,
    EmployerType EmployerHint = EmployerType.Unknown);

/// <summary>A source whose search results lack the description, so it is fetched per posting.</summary>
public interface IVacancyDetailsSource
{
    VacancySource Source { get; }

    /// <summary>Returns <c>null</c> when the posting no longer exists.</summary>
    /// <exception cref="VacancySourceUnavailableException">The source could not be reached (after retries).</exception>
    Task<VacancyDetails?> GetDetailsAsync(string externalId, CancellationToken cancellationToken);
}
