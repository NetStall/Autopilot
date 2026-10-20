using System.Text;
using JobAutopilot.Api.Features.Scoring;
using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Persistence;
using JobAutopilot.Infrastructure.SearchProfiles;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.SearchProfiles;

public sealed record SearchProfileDto(
    int Version,
    DateTimeOffset? UpdatedAt,
    SearchProfile Profile,
    IReadOnlyList<ConfiguredSearchDto> Searches);

/// <summary>Body of <c>PUT /api/search-profile</c>: the edited profile and the version it was loaded as.</summary>
public sealed record SaveSearchProfileRequest(int Version, SearchProfile? Profile);

public sealed record SaveSearchProfileResponse(SearchProfileDto SearchProfile, RescoreStatus Rescore);

public sealed record RollbackRequest(int Version, int ExpectedVersion);

/// <summary>„Mit diesen Einstellungen: 142 passende Stellen (davon 31 voll remote)" — computed, nothing saved.</summary>
public sealed record SearchProfilePreviewDto(
    IReadOnlyList<string> Errors,
    int Matching,
    int FullRemote,
    int CurrentMatching,
    int Scored,
    IReadOnlyList<ConfiguredSearchDto> Searches);

/// <summary>
/// „Suchprofil" — what I am looking for, editable in the UI. Saving validates, checks the version (409 when it was
/// changed elsewhere), keeps a history and re-scores all vacancies in the background.
/// </summary>
public static class SearchProfileEndpoints
{
    public static IEndpointRouteBuilder MapSearchProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search-profile").WithTags("Suchprofil");
        group.MapGet("/", Get).WithName("GetSearchProfile");
        group.MapPut("/", Save).WithName("SaveSearchProfile");
        group.MapPost("/preview", Preview).WithName("PreviewSearchProfile");
        group.MapGet("/history", History).WithName("SearchProfileHistory");
        group.MapPost("/rollback", Rollback).WithName("RollbackSearchProfile");
        group.MapGet("/export", Export).WithName("ExportSearchProfile");
        return app;
    }

    private static Ok<SearchProfileDto> Get(SearchProfileStore store) => TypedResults.Ok(Dto(store.Current));

    private static async Task<Results<Ok<SaveSearchProfileResponse>, ValidationProblem, ProblemHttpResult>> Save(
        SaveSearchProfileRequest request,
        SearchProfileStore store,
        RescoreCoordinator rescore,
        CancellationToken cancellationToken)
    {
        if (request.Profile is null)
        {
            return Invalid(["Das Suchprofil fehlt."]);
        }

        try
        {
            var saved = await store.SaveAsync(request.Profile, request.Version, null, cancellationToken);
            return TypedResults.Ok(new SaveSearchProfileResponse(Dto(saved), rescore.Request()));
        }
        catch (SearchProfileValidationException ex)
        {
            return Invalid(ex.Errors);
        }
        catch (SearchProfileConflictException ex)
        {
            return Conflict(ex.CurrentVersion);
        }
    }

    /// <summary>Scores all stored vacancies with the given settings in memory — nothing is saved.</summary>
    private static async Task<Ok<SearchProfilePreviewDto>> Preview(
        SearchProfile profile,
        AppDbContext db,
        IOptions<RelevanceOptions> relevance,
        RemoteClassifier remote,
        EmployerClassifier employer,
        FitAnalyzer fitAnalyzer,
        ICandidateProfileSource profiles,
        SearchProfileStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = SearchProfileRules.Validate(profile);
        var searches = errors.Count == 0 ? SearchProfileRules.GenerateSearches(profile) : [];
        var now = timeProvider.GetUtcNow();
        var vacancies = await db.Vacancies.AsNoTracking()
            .Where(v => v.Interest != VacancyInterest.Dismissed && (v.ExpiresAt == null || v.ExpiresAt >= now))
            .ToListAsync(cancellationToken);

        var current = Count(vacancies, store.Current.Profile);
        if (errors.Count > 0)
        {
            return TypedResults.Ok(new SearchProfilePreviewDto(errors, 0, 0, current.Matching, vacancies.Count, []));
        }

        var enricher = new VacancyEnricher(
            new FixedRelevanceScorerProvider(ProfileRelevanceScorerProvider.Build(relevance.Value, profile, profiles.Current.Usable)),
            remote,
            employer,
            new VacancyFitService(fitAnalyzer, profiles, timeProvider, new FixedSearchProfileSource(profile)));
        foreach (var vacancy in vacancies)
        {
            enricher.Apply(vacancy);
        }

        var preview = Count(vacancies, profile);
        return TypedResults.Ok(new SearchProfilePreviewDto(
            [], preview.Matching, preview.FullRemote, current.Matching, vacancies.Count, [.. searches.Select(ConfiguredSearchDto.From)]));
    }

    private static async Task<Ok<IReadOnlyList<SearchProfileRevisionInfo>>> History(SearchProfileStore store, CancellationToken cancellationToken) =>
        TypedResults.Ok(await store.HistoryAsync(SearchProfileStore.KeptRevisions, cancellationToken));

    private static async Task<Results<Ok<SaveSearchProfileResponse>, NotFound, ProblemHttpResult>> Rollback(
        RollbackRequest request,
        SearchProfileStore store,
        RescoreCoordinator rescore,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await store.RollbackAsync(request.Version, request.ExpectedVersion, cancellationToken);
            return saved is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(new SaveSearchProfileResponse(Dto(saved), rescore.Request()));
        }
        catch (SearchProfileConflictException ex)
        {
            return Conflict(ex.CurrentVersion);
        }
        catch (SearchProfileValidationException ex)
        {
            // An old version may no longer be valid under today's rules.
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Diese Version ist nicht mehr gültig.",
                detail: string.Join(" ", ex.Errors));
        }
    }

    /// <summary>The Suchprofil as a JSON file (to keep a copy, e.g. in git outside <c>data/</c>).</summary>
    private static FileContentHttpResult Export(SearchProfileStore store) =>
        TypedResults.File(
            Encoding.UTF8.GetBytes(SearchProfileStore.Serialize(store.Current.Profile)),
            "application/json",
            $"suchprofil-v{store.Current.Version}.json");

    private static SearchProfileDto Dto(SearchProfileSnapshot snapshot) =>
        new(snapshot.Version, snapshot.UpdatedAt, snapshot.Profile,
            [.. SearchProfileRules.GenerateSearches(snapshot.Profile).Select(ConfiguredSearchDto.From)]);

    /// <summary>The default list's view: not hidden, at least „Maybe", at least the minimum score; duplicates once.</summary>
    private static (int Matching, int FullRemote) Count(IReadOnlyList<Vacancy> vacancies, SearchProfile profile)
    {
        var hidden = profile.HiddenEmployerTypes();
        var visible = vacancies
            .Where(v => !hidden.Contains(v.EmployerType))
            .Where(v => v.RelevanceLevel is null or RelevanceLevel.Maybe or RelevanceLevel.Relevant)
            .Where(v => profile.MinScore == 0 || (v.RankScore ?? v.RelevanceScore) >= profile.MinScore)
            .GroupBy(v => v.DuplicateKey ?? v.Id.ToString())
            .Select(g => g.First())
            .ToList();
        return (visible.Count, visible.Count(v => v.RemoteLevel == RemoteLevel.FullRemote));
    }

    private static ValidationProblem Invalid(IReadOnlyList<string> errors) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["profile"] = [.. errors] }, title: "Das Suchprofil ist ungültig.");

    private static ProblemHttpResult Conflict(int currentVersion) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Das Suchprofil wurde inzwischen geändert.",
            detail: $"Aktuelle Version: {currentVersion}. Bitte neu laden und die Änderungen erneut vornehmen.");
}

/// <summary>Loads (or seeds) the Suchprofil before the import and re-scoring workers start.</summary>
public sealed partial class SearchProfileInitializer(
    SearchProfileStore store,
    ICandidateProfileSource profiles,
    ILogger<SearchProfileInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.InitializeAsync(cancellationToken);
        }
#pragma warning disable CA1031 // Without a database the defaults are used; the API still starts (health shows the DB).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(logger, ex);
        }

        if (SearchProfileSeed.HasLegacyPreferences(profiles.Current.Profile))
        {
            LogLegacy(logger);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suchprofil could not be loaded; using the configuration defaults until the next start")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "profile.json still contains 'preferences'. They are no longer used: edit them on the page Suchprofil and remove them from the file.")]
    private static partial void LogLegacy(ILogger logger);
}

/// <summary>The searches an import without request body runs: generated from the Suchprofil.</summary>
public static class SearchProfileImports
{
    public static (IReadOnlyList<VacancySearch> Searches, IReadOnlyList<string> TitleExclusions) Current(ISearchProfileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var profile = source.Current.Profile;
        return (SearchProfileRules.GenerateSearches(profile), profile.TitleExclusions);
    }
}
