using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace JobAutopilot.Infrastructure.Arbeitsagentur;

/// <summary>
/// Searches the Bundesagentur für Arbeit Jobsuche API (typed <see cref="HttpClient"/>, see
/// <see cref="DependencyInjection"/>). Pages are requested one after another with a pause in between.
/// </summary>
internal sealed partial class ArbeitsagenturClient(
    HttpClient httpClient,
    IOptions<ArbeitsagenturOptions> options,
    TimeProvider timeProvider,
    ILogger<ArbeitsagenturClient> logger) : IVacancySource
{
    internal const string SearchPath = "pc/v6/jobs";
    internal const string UserAgent = "job-autopilot/0.2 (+https://github.com/NetStall/Autopilot)";

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
        var skipped = 0;

        for (var page = 1; page <= settings.MaxPages; page++)
        {
            if (page > 1 && settings.DelayBetweenPagesMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(settings.DelayBetweenPagesMs), timeProvider, cancellationToken);
            }

            var response = await GetPageAsync(BuildSearchUri(search, page, settings.PageSize), page, cancellationToken);
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

            if (items.Count == 0 || page * settings.PageSize >= response.MaxErgebnisse)
            {
                break;
            }
        }

        if (skipped > 0)
        {
            LogEntriesSkipped(logger, skipped, search.Keyword);
        }
    }

    internal static string BuildSearchUri(VacancySearch search, int page, int pageSize)
    {
        var query = new List<string>
        {
            "was=" + Uri.EscapeDataString(search.Keyword.Trim()),
            "angebotsart=" + EmploymentOfferType,
        };

        if (!string.IsNullOrWhiteSpace(search.Location))
        {
            query.Add("wo=" + Uri.EscapeDataString(search.Location.Trim()));
            if (search.RadiusKm is { } radius)
            {
                query.Add("umkreis=" + radius.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (search.PublishedWithinDays is { } days)
        {
            query.Add("veroeffentlichtseit=" + days.ToString(CultureInfo.InvariantCulture));
        }

        query.Add("page=" + page.ToString(CultureInfo.InvariantCulture));
        query.Add("size=" + pageSize.ToString(CultureInfo.InvariantCulture));

        return SearchPath + "?" + string.Join('&', query);
    }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped {Count} unusable search results for keyword {Keyword}")]
    private static partial void LogEntriesSkipped(ILogger logger, int count, string keyword);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Arbeitsagentur request for page {Page} failed")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, int page);
}
