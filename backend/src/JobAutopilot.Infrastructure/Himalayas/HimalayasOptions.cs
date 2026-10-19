using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Himalayas;

/// <summary>Settings for the Himalayas remote jobs API (configuration section <c>Himalayas</c>, ADR 0008).</summary>
public sealed class HimalayasOptions
{
    public const string SectionName = "Himalayas";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://himalayas.app/";

    /// <summary>ISO country code: jobs open to applicants living there.</summary>
    [Required]
    [RegularExpression("^[A-Z]{2}$")]
    public string Country { get; set; } = "DE";

    /// <summary>Leave out "worldwide" jobs (no location restriction) — mostly US-centred marketplaces.</summary>
    public bool ExcludeWorldwide { get; set; } = true;

    /// <summary>The search returns 20 jobs per page; default limit per search, a search can override it.</summary>
    [Range(1, 20)]
    public int MaxPages { get; set; } = 5;

    /// <summary>Pause between two requests. Unkeyed requests share a public allowance; the data refreshes daily.</summary>
    [Range(0, 60_000)]
    public int DelayBetweenPagesMs { get; set; } = 1500;

    /// <summary>Optional API key (user-secrets / environment only, never in appsettings) — sent as a Bearer token.</summary>
    public string? ApiKey { get; set; }
}

[OptionsValidator]
internal sealed partial class HimalayasOptionsValidator : IValidateOptions<HimalayasOptions>;
