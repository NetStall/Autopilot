using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.Settings;

/// <summary>Configuration section <c>Ui</c>: presentation settings the frontend loads at start.</summary>
public sealed class UiOptions
{
    public const string SectionName = "Ui";

    /// <summary>Terms highlighted in vacancy titles — a visual cue only, not scoring.</summary>
    public List<string> HighlightKeywords { get; init; } = [];

    /// <summary>Score badge colours: from here on „hoch" …</summary>
    public int ScoreHigh { get; init; } = 70;

    /// <summary>… from here on „mittel", below „niedrig".</summary>
    public int ScoreMedium { get; init; } = 45;
}

public sealed record UiSettingsDto(IReadOnlyList<string> HighlightKeywords, int ScoreHigh, int ScoreMedium);

public static class UiSettingsEndpoints
{
    public static IEndpointRouteBuilder MapUiSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings/ui", GetUiSettings).WithTags("Settings").WithName(nameof(GetUiSettings));
        return app;
    }

    private static Ok<UiSettingsDto> GetUiSettings(IOptions<UiOptions> options) =>
        TypedResults.Ok(new UiSettingsDto(
            [.. options.Value.HighlightKeywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct()],
            options.Value.ScoreHigh,
            options.Value.ScoreMedium));
}
