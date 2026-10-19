using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.Settings;

/// <summary>Configuration section <c>Ui</c>: presentation settings the frontend loads at start.</summary>
public sealed class UiOptions
{
    public const string SectionName = "Ui";

    /// <summary>Terms highlighted in vacancy titles — a visual cue only, not scoring.</summary>
    public List<string> HighlightKeywords { get; init; } = [];
}

public sealed record UiSettingsDto(IReadOnlyList<string> HighlightKeywords);

public static class UiSettingsEndpoints
{
    public static IEndpointRouteBuilder MapUiSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings/ui", GetUiSettings).WithTags("Settings").WithName(nameof(GetUiSettings));
        return app;
    }

    private static Ok<UiSettingsDto> GetUiSettings(IOptions<UiOptions> options) =>
        TypedResults.Ok(new UiSettingsDto(
            [.. options.Value.HighlightKeywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct()]));
}
