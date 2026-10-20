using System.Globalization;
using JobAutopilot.Core.Profile;

namespace JobAutopilot.Core.Documents;

/// <summary>Picks the German or the base text of a profile item.</summary>
public static class Localized
{
    /// <summary>The summary of a focus: <c>fullstack.de</c> for German if present, else <c>fullstack</c>.</summary>
    public static string? Summary(CandidateProfile profile, string focus, DocumentLanguage language)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (language == DocumentLanguage.De && profile.Summary.TryGetValue($"{focus}.de", out var german) && !string.IsNullOrWhiteSpace(german))
        {
            return german;
        }

        return profile.Summary.TryGetValue(focus, out var text) ? text : null;
    }
}

/// <summary>German / English formats for documents (InvariantGlobalization is on: no culture data, formats by hand).</summary>
public static class Formats
{
    private static readonly string[] GermanMonths =
    [
        "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember",
    ];

    private static readonly string[] EnglishMonths =
    [
        "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
    ];

    /// <summary>„01.11.2026" / „1 November 2026".</summary>
    public static string Date(DateOnly date, DocumentLanguage language) => language == DocumentLanguage.De
        ? date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
        : $"{date.Day} {EnglishMonths[date.Month - 1]} {date.Year}";

    /// <summary>A <c>YYYY-MM-DD</c> value as <see cref="Date(DateOnly, DocumentLanguage)"/>; anything else unchanged.</summary>
    public static string Date(string value, DocumentLanguage language) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? Date(date, language)
            : value;

    /// <summary>The letter's date line: „20. Oktober 2026" / „20 October 2026".</summary>
    public static string LongDate(DateOnly date, DocumentLanguage language) => language == DocumentLanguage.De
        ? $"{date.Day}. {GermanMonths[date.Month - 1]} {date.Year}"
        : $"{date.Day} {EnglishMonths[date.Month - 1]} {date.Year}";

    /// <summary>„55.000 €" / „€55,000".</summary>
    public static string Euro(int amount, DocumentLanguage language)
    {
        var digits = amount.ToString("#,0", CultureInfo.InvariantCulture);
        return language == DocumentLanguage.De ? $"{digits.Replace(',', '.')} €" : $"€{digits}";
    }

    /// <summary>„55.000–60.000 € brutto/Jahr", „ab 55.000 € …"; <c>null</c> without any value.</summary>
    public static string? SalaryRange(int? min, int? max, DocumentLanguage language)
    {
        var suffix = language == DocumentLanguage.De ? " brutto/Jahr" : " gross per year";
        return (min, max) switch
        {
            ({ } a, { } b) when a != b => language == DocumentLanguage.De
                ? $"{Euro(a, language).Replace(" €", "", StringComparison.Ordinal)}–{Euro(b, language)}{suffix}"
                : $"{Euro(a, language)}–{Euro(b, language)}{suffix}",
            ({ } a, _) => Euro(a, language) + suffix,
            (null, { } b) => Euro(b, language) + suffix,
            _ => null,
        };
    }
}

/// <summary>Periods of profile entries: „07/2022 – 09/2025", „seit 07/2022" / „since 07/2022".</summary>
public static class Dates
{
    public static string Month(string? value) =>
        ProfileValidator.TryParseMonth(value, out var month) ? month.ToString("MM/yyyy", CultureInfo.InvariantCulture) : value ?? string.Empty;

    public static string Period(string from, string? to, DocumentLanguage language) => to is null
        ? $"{(language == DocumentLanguage.De ? "seit" : "since")} {Month(from)}"
        : $"{Month(from)} – {Month(to)}";
}

/// <summary>Links as they are printed: without scheme, „www." and trailing slash.</summary>
public static class Links
{
    public static string Display(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var text = url.Trim();
        foreach (var prefix in new[] { "https://", "http://" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..];
            }
        }

        if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            text = text[4..];
        }

        return text.TrimEnd('/');
    }
}
