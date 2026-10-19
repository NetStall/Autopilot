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
    public int PageSize { get; set; } = 100;

    /// <summary>Default page limit per search; a search can override it.</summary>
    [Range(1, 50)]
    public int MaxPages { get; set; } = 10;

    /// <summary>Pause between two page requests of one search, to be polite to a public service.</summary>
    [Range(0, 10_000)]
    public int DelayBetweenPagesMs { get; set; } = 300;

    /// <summary>
    /// Value of the <c>homeoffice</c> parameter for <c>RemoteOnly</c> searches. <c>prozentual_0;nv_true</c> (default)
    /// = any home office incl. "by arrangement" — the web app's filter; how remote a posting really is comes from
    /// <c>homeofficeprozent</c> and the description (RemoteLevel). <c>prozentual_100</c> = only postings that state
    /// 100 % (very few). See docs/arbeitsagentur-api.md.
    /// </summary>
    [Required]
    [RegularExpression("^(prozentual_(100|[1-9]?[0-9])(;nv_true)?|nv_true)$")]
    public string RemoteHomeOfficeFilter { get; set; } = "prozentual_0;nv_true";
}

[OptionsValidator]
internal sealed partial class ArbeitsagenturOptionsValidator : IValidateOptions<ArbeitsagenturOptions>;
