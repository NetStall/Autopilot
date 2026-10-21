using System.Globalization;
using System.Text.RegularExpressions;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Scoring;

/// <summary>Where the remote level comes from.</summary>
public enum RemoteSource
{
    None,

    /// <summary>The source's share field (Arbeitsagentur „Homeoffice (bis zu 40 %)").</summary>
    Field,

    /// <summary>The source says only „Homeoffice möglich" (no share) or lists the job as remote (Himalayas).</summary>
    Flag,

    /// <summary>A phrase or a stated share in the posting.</summary>
    Text,

    /// <summary>The model's extraction, when the rules found nothing.</summary>
    Extraction,
}

/// <summary>
/// One answer to „how much home office?" (step 07b, part 0.1): the level, the share when known, where it comes from,
/// whether it is clearly partial („teilweise im Homeoffice", „hybrides Arbeiten im Headquarter", „2 Tage vor Ort")
/// and a short German label for the badge. <see cref="Conflict"/> names a disagreement between field and text.
/// </summary>
public sealed record RemoteAssessment(RemoteLevel Level, int? Percent, RemoteSource Source, bool Partial, string Label, string? Conflict)
{
    public static readonly RemoteAssessment Unknown = new(RemoteLevel.Unknown, null, RemoteSource.None, false, "Homeoffice: unbekannt", null);
}

/// <summary>
/// Combines the signals into a <see cref="RemoteAssessment"/>. A stated share wins (field first, then the text); a bare
/// „Homeoffice möglich" flag only means „some home office" and never outranks a text cue; the more specific signal
/// wins and a disagreement is reported. Himalayas' „Remote (…)" stays full remote unless the text says on site.
/// </summary>
public static partial class RemoteAssessor
{
    public static RemoteAssessment Assess(
        RemoteClassification text,
        string? partialCue,
        int? textShare,
        RemoteLevel hint,
        int? fieldPercent,
        Extraction.RequirementExtraction? extraction,
        bool remoteListing)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (fieldPercent is { } field and < 100)
        {
            return new(RemoteLevel.Hybrid, field, RemoteSource.Field, true, $"Homeoffice: bis {Pct(field)} (Feld)", null);
        }

        if (fieldPercent is not null)
        {
            // „100 %" in the field, but „anteilig Homeoffice" / „teilweise im Homeoffice" in the text: the text is more
            // specific (a wrong field is common), the share stays unknown.
            var cue = partialCue ?? (text.Level == RemoteLevel.Hybrid ? text.Reason : null);
            return cue is null
                ? new(RemoteLevel.FullRemote, 100, RemoteSource.Field, false, "Homeoffice: 100 % (Feld)", null)
                : new(RemoteLevel.Hybrid, null, RemoteSource.Text, partialCue is not null,
                    partialCue is not null ? "Homeoffice: teilweise (laut Text)" : "Homeoffice möglich (laut Text)", $"Feld 100 %, Text: {cue}");
        }

        if (text.Level == RemoteLevel.FullRemote)
        {
            return new(RemoteLevel.FullRemote, 100, RemoteSource.Text, false, "Voll remote (laut Text)", null);
        }

        if (text.Level == RemoteLevel.OnSite)
        {
            // „kein Homeoffice" against a remote search hit or the source's flag: hybrid as before, but clearly not remote.
            return hint >= RemoteLevel.Hybrid
                ? new(RemoteLevel.Hybrid, null, RemoteSource.Text, true, "Homeoffice: kaum (laut Text)", $"Quelle: Homeoffice, Text: {text.Reason}")
                : new(RemoteLevel.OnSite, 0, RemoteSource.Text, true, "Nur vor Ort (laut Text)", null);
        }

        if (textShare is { } share)
        {
            return share >= 100
                ? new(RemoteLevel.FullRemote, 100, RemoteSource.Text, false, "Voll remote (laut Text)", null)
                : new(RemoteLevel.Hybrid, share, RemoteSource.Text, true, $"Homeoffice: bis {Pct(share)} (laut Text)", null);
        }

        if (partialCue is not null)
        {
            var conflict = remoteListing || hint == RemoteLevel.FullRemote ? $"Quelle: voll remote, Text: „{partialCue}“" : null;
            return new(RemoteLevel.Hybrid, null, RemoteSource.Text, true, "Homeoffice: teilweise (laut Text)", conflict);
        }

        if (remoteListing)
        {
            // „work from home, the office or in a hybrid setup" in the perks of a remote listing is an offer, not a limit.
            return new(RemoteLevel.FullRemote, 100, RemoteSource.Flag, false, "Voll remote (laut Quelle)", null);
        }

        if (extraction is not null && extraction.RemoteLevel != RemoteLevel.Unknown)
        {
            return extraction.RemoteLevel switch
            {
                RemoteLevel.FullRemote => new(RemoteLevel.FullRemote, 100, RemoteSource.Extraction, false, "Voll remote (laut KI)", null),
                RemoteLevel.OnSite => new(RemoteLevel.OnSite, 0, RemoteSource.Extraction, true, "Nur vor Ort (laut KI)", null),
                _ => new(RemoteLevel.Hybrid, null, RemoteSource.Extraction, false, "Homeoffice möglich (laut KI)", null),
            };
        }

        if (text.Level == RemoteLevel.Hybrid)
        {
            return new(RemoteLevel.Hybrid, null, RemoteSource.Text, false, "Homeoffice möglich (laut Text)",
                hint == RemoteLevel.FullRemote ? $"Quelle: voll remote, Text: {text.Reason}" : null);
        }

        return hint switch
        {
            RemoteLevel.FullRemote => new(RemoteLevel.FullRemote, 100, RemoteSource.Flag, false, "Voll remote (laut Quelle)", null),
            RemoteLevel.Hybrid => new(RemoteLevel.Hybrid, null, RemoteSource.Flag, false, "Homeoffice möglich (Feld, ohne Anteil)", null),
            _ => RemoteAssessment.Unknown,
        };
    }

    /// <summary>„2 Tage pro Woche vor Ort", „3 days a week in the office", „Präsenztage".</summary>
    public static string? OnSiteDays(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : OnSiteDaysPattern().Match(text) is { Success: true } m ? m.Value.Trim() : null;

    private static string Pct(int percent) => percent.ToString(CultureInfo.InvariantCulture) + " %";

    [GeneratedRegex(@"\b(?:\d|ein(?:en)?|zwei|drei|vier|one|two|three|four)\s+(?:Tage?n?|days?)\s+(?:pro|die|per|a|im|in\s+der|each)\s+(?:Woche|Monat|week|month)\s+(?:vor\s+Ort|im\s+Büro|im\s+Office|in\s+the\s+office|on[- ]site)|\bPräsenztage?\b|\bPräsenzpflicht\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex OnSiteDaysPattern();
}
