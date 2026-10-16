using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Arbeitsagentur;

/// <summary>Settings for the Bundesagentur für Arbeit Jobsuche API (configuration section <c>Arbeitsagentur</c>).</summary>
public sealed class ArbeitsagenturOptions
{
    public const string SectionName = "Arbeitsagentur";

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://rest.arbeitsagentur.de/jobboerse/jobsuche-service/";

    /// <summary>The public client id of the official job board app — not a secret.</summary>
    [Required]
    public string ApiKey { get; set; } = "jobboerse-jobsuche";

    [Range(1, 100)]
    public int PageSize { get; set; } = 50;

    [Range(1, 50)]
    public int MaxPages { get; set; } = 5;

    /// <summary>Pause between two page requests of one search, to be polite to a public service.</summary>
    [Range(0, 10_000)]
    public int DelayBetweenPagesMs { get; set; } = 300;
}

[OptionsValidator]
internal sealed partial class ArbeitsagenturOptionsValidator : IValidateOptions<ArbeitsagenturOptions>;
