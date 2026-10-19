using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace JobAutopilot.Infrastructure.Arbeitsagentur;

/// <summary>
/// Searches the Bundesagentur für Arbeit Jobsuche API and fetches posting details (typed <see cref="HttpClient"/>,
/// see <see cref="DependencyInjection"/>). Pages are requested one after another with a pause in between.
/// </summary>
internal sealed partial class ArbeitsagenturClient(
    HttpClient httpClient,
    IOptions<ArbeitsagenturOptions> options,
    TimeProvider timeProvider,
    ILogger<ArbeitsagenturClient> logger) : IVacancySource, IVacancyDetailsSource
{
    internal const string SearchPath = "pc/v6/jobs";
    internal const string DetailsPath = "pc/v4/jobdetails/";
    internal const string UserAgent = "job-autopilot/0.4 (+https://github.com/NetStall/Autopilot)";

    /// <summary>1 = Arbeit (employment); excludes apprenticeships, internships and self-employment.</summary>
    private const string EmploymentOfferType = "1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public VacancySource Source => VacancySource.Arbeitsagentur;

    public async IAsyncEnumerable<VacancyCandidate> SearchAsync(
        VacancySearch search,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var settings = options.Value;
        var maxPages = search.MaxPages ?? settings.MaxPages;
        var skipped = 0;

        for (var page = 1; page <= maxPages; page++)
        {
            if (page > 1 && settings.DelayBetweenPagesMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(settings.DelayBetweenPagesMs), timeProvider, cancellationToken);
            }

            var requestUri = BuildSearchUri(search, page, settings.PageSize, settings.RemoteHomeOfficeFilter);
            var response = await GetPageAsync(requestUri, page, cancellationToken);
            var items = response.Ergebnisliste ?? [];

            foreach (var item in items)
            {
                var candidate = ArbeitsagenturMapper.ToCandidate(item);
                if (candidate is null)
                {
                    skipped++;
                    LogEntrySkipped(logger, page);
                    continue;
                }

                yield return candidate;
            }

            var reachedEnd = items.Count == 0 || page * settings.PageSize >= response.MaxErgebnisse;
            if (reachedEnd)
            {
                break;
            }

            if (page == maxPages)
            {
                LogPageLimitReached(logger, search.Name, maxPages, page * settings.PageSize, response.MaxErgebnisse);
            }
        }

        if (skipped > 0)
        {
            LogEntriesSkipped(logger, skipped, search.Name);
        }
    }

    internal static string BuildSearchUri(VacancySearch search, int page, int pageSize, string remoteHomeOfficeFilter)
    {
        var query = new List<string>
        {
            "was=" + Uri.EscapeDataString(search.Keyword.Trim()),
            "angebotsart=" + EmploymentOfferType,
        };

        // Without "wo" the Jobsuche searches all of Germany.
        if (!string.IsNullOrWhiteSpace(search.Location))
        {
            query.Add("wo=" + Uri.EscapeDataString(search.Location.Trim()));
            if (search.RadiusKm is { } radius)
            {
                query.Add("umkreis=" + radius.ToString(CultureInfo.InvariantCulture));
            }
        }

        // "arbeitszeit=ho" returns nothing in v6; home office is its own parameter (docs/arbeitsagentur-api.md).
        if (search.RemoteOnly)
        {
            query.Add("homeoffice=" + Uri.EscapeDataString(remoteHomeOfficeFilter));
        }

        query.Add("veroeffentlichtseit=" + search.PublishedWithinDays.ToString(CultureInfo.InvariantCulture));
        query.Add("page=" + page.ToString(CultureInfo.InvariantCulture));
        query.Add("size=" + pageSize.ToString(CultureInfo.InvariantCulture));

        return SearchPath + "?" + string.Join('&', query);
    }

    public async Task<VacancyDetails?> GetDetailsAsync(string externalId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        try
        {
            using var response = await httpClient.GetAsync(BuildDetailsUri(externalId), cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return null; // the posting was taken offline
            }

            response.EnsureSuccessStatusCode();
            var details = await response.Content.ReadFromJsonAsync<JobDetailsResponse>(JsonOptions, cancellationToken)
                ?? throw new JsonException("The response body was empty.");
            return ArbeitsagenturMapper.ToDetails(details);
        }
        catch (Exception ex) when (IsSourceFailure(ex, cancellationToken))
        {
            LogDetailsFailed(logger, ex, externalId);
            throw new VacancySourceUnavailableException(
                $"The Arbeitsagentur details for {externalId} could not be fetched: {ex.Message}", ex);
        }
    }

    /// <summary>The details endpoint takes the reference number Base64-encoded (verified 2026-10-19).</summary>
    internal static string BuildDetailsUri(string externalId) =>
        DetailsPath + Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(externalId.Trim())));

    private async Task<JobSearchResponse> GetPageAsync(string requestUri, int page, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<JobSearchResponse>(JsonOptions, cancellationToken)
                ?? throw new JsonException("The response body was empty.");
        }
        catch (Exception ex) when (IsSourceFailure(ex, cancellationToken))
        {
            LogRequestFailed(logger, ex, page);
            throw new VacancySourceUnavailableException(
                $"The Arbeitsagentur Jobsuche API could not be used (page {page}): {ex.Message}", ex);
        }
    }

    private static bool IsSourceFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or JsonException or TimeoutRejectedException or BrokenCircuitException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped a search result without reference number or title on page {Page}")]
    private static partial void LogEntrySkipped(ILogger logger, int page);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped {Count} unusable search results in search {SearchName}")]
    private static partial void LogEntriesSkipped(ILogger logger, int count, string searchName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Search {SearchName} stopped at MaxPages {MaxPages} after {Requested} results although the API reports {Total}")]
    private static partial void LogPageLimitReached(ILogger logger, string searchName, int maxPages, int requested, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Arbeitsagentur details request for {ExternalId} failed")]
    private static partial void LogDetailsFailed(ILogger logger, Exception exception, string externalId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Arbeitsagentur request for page {Page} failed")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, int page);
}
