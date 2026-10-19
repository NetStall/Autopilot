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

namespace JobAutopilot.Infrastructure.Himalayas;

/// <summary>
/// Searches the Himalayas remote jobs API for jobs open to applicants in Germany (typed <see cref="HttpClient"/>,
/// see <see cref="DependencyInjection"/>). Pages one after another with a pause; 429 is retried by the resilience
/// handler (honours <c>Retry-After</c>). Terms and attribution: ADR 0008.
/// </summary>
internal sealed partial class HimalayasClient(
    HttpClient httpClient,
    IOptions<HimalayasOptions> options,
    TimeProvider timeProvider,
    ILogger<HimalayasClient> logger) : IVacancySource
{
    internal const string SearchPath = "jobs/api/search";
    internal const int PageSize = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public VacancySource Source => VacancySource.Himalayas;

    public async IAsyncEnumerable<VacancyCandidate> SearchAsync(
        VacancySearch search,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var settings = options.Value;
        var maxPages = search.MaxPages ?? settings.MaxPages;
        var publishedAfter = timeProvider.GetUtcNow().AddDays(-search.PublishedWithinDays);
        var skipped = 0;

        for (var page = 1; page <= maxPages; page++)
        {
            if (page > 1 && settings.DelayBetweenPagesMs > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(settings.DelayBetweenPagesMs), timeProvider, cancellationToken);
            }

            var response = await GetPageAsync(BuildSearchUri(search, page, settings), page, cancellationToken);
            var jobs = response.Jobs ?? [];

            foreach (var job in jobs)
            {
                var candidate = HimalayasMapper.ToCandidate(job, settings.Country);
                if (candidate is null)
                {
                    skipped++;
                    continue;
                }

                if (candidate.PublishedAt is { } published && published < publishedAfter)
                {
                    continue; // older than the search asks for
                }

                yield return candidate;
            }

            if (jobs.Count == 0 || page * PageSize >= response.TotalCount)
            {
                break;
            }
        }

        if (skipped > 0)
        {
            LogEntriesSkipped(logger, skipped, search.Name);
        }
    }

    internal static string BuildSearchUri(VacancySearch search, int page, HimalayasOptions settings)
    {
        var query = new List<string>
        {
            "q=" + Uri.EscapeDataString(search.Keyword.Trim()),
            "country=" + Uri.EscapeDataString(settings.Country),
        };
        if (settings.ExcludeWorldwide)
        {
            query.Add("exclude_worldwide=true");
        }

        query.Add("sort=recent");
        query.Add("page=" + page.ToString(CultureInfo.InvariantCulture));
        return SearchPath + "?" + string.Join('&', query);
    }

    private async Task<HimalayasSearchResponse> GetPageAsync(string requestUri, int page, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<HimalayasSearchResponse>(JsonOptions, cancellationToken)
                ?? throw new JsonException("The response body was empty.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TimeoutRejectedException or BrokenCircuitException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogRequestFailed(logger, ex, page);
            throw new VacancySourceUnavailableException(
                $"The Himalayas API could not be used (page {page}): {ex.Message}", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped {Count} unusable Himalayas jobs in search {SearchName}")]
    private static partial void LogEntriesSkipped(ILogger logger, int count, string searchName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Himalayas request for page {Page} failed")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, int page);
}
