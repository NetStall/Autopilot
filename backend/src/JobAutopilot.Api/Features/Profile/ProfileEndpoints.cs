using JobAutopilot.Core.Profile;
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

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/profile", GetProfile).WithTags("Profile").WithName(nameof(GetProfile));
        return app;
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
