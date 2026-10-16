using System.ComponentModel.DataAnnotations;
using JobAutopilot.Core.Import;

namespace JobAutopilot.Api.Features.VacancyImports;

/// <summary>A single ad-hoc search; same shape as an entry of <c>Import:Searches</c>.</summary>
public sealed class ImportSearchRequest
{
    [Required]
    [MaxLength(200)]
    public string Keyword { get; init; } = string.Empty;

    [MaxLength(200)]
    public string? Location { get; init; }

    [Range(0, 200)]
    public int? RadiusKm { get; init; }

    [Range(0, 100)]
    public int? PublishedWithinDays { get; init; }

    public VacancySearch ToSearch() =>
        new(Keyword.Trim(), string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(), RadiusKm, PublishedWithinDays);
}
