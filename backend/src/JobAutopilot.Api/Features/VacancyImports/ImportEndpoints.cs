using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.VacancyImports;

public static partial class ImportEndpoints
{
    private const string LogCategory = "JobAutopilot.Api.Imports";

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").WithTags("Imports");

        group.MapPost("/arbeitsagentur", ImportFromArbeitsagentur).WithName(nameof(ImportFromArbeitsagentur));

        return app;
    }

    /// <summary>
    /// Runs the configured searches (empty body) or the one search in the body against the Arbeitsagentur
    /// and stores the results. Triggered manually only.
    /// </summary>
    private static async Task<Results<Ok<ImportResultDto>, ProblemHttpResult>> ImportFromArbeitsagentur(
        ImportSearchRequest? request,
        IEnumerable<IVacancySource> sources,
        VacancyImportService importService,
        IOptions<ImportOptions> importOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var source = sources.Single(s => s.Source == VacancySource.Arbeitsagentur);
        IReadOnlyList<VacancySearch> searches = request is null ? importOptions.Value.Searches : [request.ToSearch()];
        var logger = loggerFactory.CreateLogger(LogCategory);
        var started = timeProvider.GetTimestamp();

        ImportResult result;
        try
        {
            var exclusion = new TitleExclusionFilter(importOptions.Value.ExcludeTitleTerms);
            result = await importService.ImportAsync(source, searches, exclusion, cancellationToken);
        }
        catch (VacancySourceUnavailableException ex)
        {
            LogImportFailed(logger, ex);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The Arbeitsagentur Jobsuche API is not reachable.",
                detail: "Nothing was imported. " + ex.Message);
        }

        foreach (var search in result.Searches)
        {
            LogSearchImported(
                logger,
                search.Search.Name,
                search.Search.Keyword,
                search.Search.Location,
                search.Fetched,
                search.Added,
                search.Updated,
                search.Unchanged,
                search.Excluded);
        }

        var duration = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        return TypedResults.Ok(new ImportResultDto(result.Fetched, result.Added, result.Updated, result.Unchanged, result.Excluded, duration));
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Imported search {SearchName} ({Keyword} in {Location}): fetched {Fetched}, added {Added}, updated {Updated}, unchanged {Unchanged}, excluded {Excluded}")]
    private static partial void LogSearchImported(
        ILogger logger,
        string searchName,
        string keyword,
        string? location,
        int fetched,
        int added,
        int updated,
        int unchanged,
        int excluded);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Arbeitsagentur import failed; nothing was saved")]
    private static partial void LogImportFailed(ILogger logger, Exception exception);
}
