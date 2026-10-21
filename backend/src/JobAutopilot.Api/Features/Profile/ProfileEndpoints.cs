using System.Text.Json;
using JobAutopilot.Core.Llm;
using JobAutopilot.Core.Profile;
using JobAutopilot.Infrastructure.Profile;
using Microsoft.AspNetCore.Http.HttpResults;

namespace JobAutopilot.Api.Features.Profile;

/// <summary>
/// <c>GET /api/profile</c>: the loaded candidate profile with its validation problems (read-only; editing stays in the
/// JSON file). Personal data — localhost only like everything else (docs/SECURITY.md).
/// </summary>
public sealed record ProfileDto(
    ProfileStatus Status,
    string FileName,
    DateTimeOffset? LoadedAt,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    double? ExperienceYears,
    CandidateProfile? Profile);

/// <summary>Drafted German texts for the review (<c>POST /api/profile/translations</c>).</summary>
public sealed record TranslationDraftDto(IReadOnlyList<TranslationSuggestion> Suggestions, int Missing, int InputTokens, int OutputTokens);

/// <summary>Body of <c>POST /api/profile/translations/apply</c>: the file content and the texts I approved.</summary>
public sealed record ApplyTranslationsRequest(string? Json, IReadOnlyList<ProfileTranslation>? Translations);

/// <summary>The file content with the approved texts inserted — the script writes it, the API never does.</summary>
public sealed record ApplyTranslationsDto(string Json, int Inserted);

public static class ProfileEndpoints
{
    private static readonly JsonSerializerOptions ProfileJson = FileCandidateProfileSource.JsonOptions;

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/profile", GetProfile).WithTags("Profile").WithName(nameof(GetProfile));

        // „Fehlende deutsche Texte übersetzen" (06c): list, draft with the letter model, insert after my approval.
        var translations = app.MapGroup("/api/profile/translations").WithTags("Profile");
        translations.MapGet("/", (ProfileTranslationService service) => TypedResults.Ok(service.Missing())).WithName("GetMissingTranslations");
        translations.MapPost("/", DraftTranslations).WithName(nameof(DraftTranslations));
        translations.MapPost("/apply", ApplyTranslations).WithName(nameof(ApplyTranslations));
        return app;
    }

    private static async Task<Results<Ok<TranslationDraftDto>, ProblemHttpResult>> DraftTranslations(
        ProfileTranslationService service, CancellationToken cancellationToken)
    {
        if (!service.IsAvailable)
        {
            return TypedResults.Problem("Kein Profil geladen oder kein Modell für „Letter“ eingerichtet.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var (suggestions, usage) = await service.DraftAsync(cancellationToken);
            return TypedResults.Ok(new TranslationDraftDto(suggestions, service.Missing().Count, usage.InputTokens, usage.OutputTokens));
        }
        catch (LlmException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Übersetzung nicht möglich");
        }
    }

    private static Results<Ok<ApplyTranslationsDto>, ValidationProblem> ApplyTranslations(ApplyTranslationsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Json) || request.Json.Length > 1_000_000 || request.Translations is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["json"] = ["The profile JSON and the approved translations are required."] });
        }

        try
        {
            var profile = JsonSerializer.Deserialize<CandidateProfile>(request.Json, ProfileJson) ?? new CandidateProfile();
            var missing = ProfileTranslations.Missing(profile);
            var updated = ProfileTranslations.Insert(request.Json, missing, request.Translations);
            var after = ProfileTranslations.Missing(JsonSerializer.Deserialize<CandidateProfile>(updated, ProfileJson) ?? new CandidateProfile());
            return TypedResults.Ok(new ApplyTranslationsDto(updated, missing.Count - after.Count));
        }
        catch (JsonException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["json"] = [$"The profile is no valid JSON: {ex.Message}"] });
        }
    }

    private static Ok<ProfileDto> GetProfile(ICandidateProfileSource profiles, TimeProvider timeProvider)
    {
        var snapshot = profiles.Current;
        double? years = snapshot.Profile is { } profile
            ? Math.Round(ProfileValidator.ExperienceMonths(profile, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)) / 12.0, 1)
            : null;
        return TypedResults.Ok(new ProfileDto(
            snapshot.Status, snapshot.FileName, snapshot.LoadedAt, snapshot.Errors, snapshot.Warnings, years, snapshot.Profile));
    }
}
