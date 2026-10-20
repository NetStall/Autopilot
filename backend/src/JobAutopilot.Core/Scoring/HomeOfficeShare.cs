using System.Globalization;
using System.Text.RegularExpressions;

namespace JobAutopilot.Core.Scoring;

/// <summary>
/// The share of home office a posting names: „Homeoffice bis zu 40 %", „60 % remote", „2 Tage Homeoffice" (a day = 20
/// %). The highest share found wins; <c>null</c> when none is named.
/// </summary>
public static partial class HomeOfficeShare
{
    private static readonly Dictionary<string, int> DayWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ein"] = 1, ["eine"] = 1, ["einen"] = 1, ["einem"] = 1, ["one"] = 1, ["zwei"] = 2, ["two"] = 2, ["drei"] = 3, ["three"] = 3,
        ["vier"] = 4, ["four"] = 4,
    };

    public static int? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        int? best = null;
        foreach (Match match in PercentBefore().Matches(text))
        {
            Consider(int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture));
        }

        foreach (Match match in PercentAfter().Matches(text))
        {
            Consider(int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture));
        }

        foreach (Match match in DaysBefore().Matches(text))
        {
            ConsiderDays(match.Groups["n"].Value);
        }

        foreach (Match match in DaysAfter().Matches(text))
        {
            ConsiderDays(match.Groups["n"].Value);
        }

        return best;

        void ConsiderDays(string value)
        {
            var days = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : DayWords.GetValueOrDefault(value);
            if (days is >= 1 and <= 5)
            {
                Consider(days * 20);
            }
        }

        void Consider(int percent)
        {
            if (percent is > 0 and <= 100 && (best is null || percent > best))
            {
                best = percent;
            }
        }
    }

    /// <summary>„40 % Homeoffice", „60% remote", „80 % mobiles Arbeiten".</summary>
    [GeneratedRegex(@"\b(?<n>\d{1,3})\s?(?:%|Prozent|percent)\s+(?:\w+\s+){0,2}(?:Home[\s-]?Office|Remote|mobile[sn]?\s+Arbeit\w*|Telearbeit)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex PercentBefore();

    /// <summary>„Homeoffice bis zu 40 %", „Remote-Anteil von 60 %", „mobiles Arbeiten zu 50%".</summary>
    [GeneratedRegex(@"(?:Home[\s-]?Office|Remote|mobile[sn]?\s+Arbeit\w*|Telearbeit)[^.\n%]{0,40}?\b(?<n>\d{1,3})\s?(?:%|Prozent|percent)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex PercentAfter();

    /// <summary>„2 Tage Homeoffice", „zwei Tage pro Woche remote", „3 days remote".</summary>
    [GeneratedRegex(@"\b(?<n>[1-5]|ein(?:e[nm]?)?|zwei|drei|vier|one|two|three|four)\s+(?:Tage?n?|days?)\s+(?:\w+\s+){0,3}(?:im\s+)?(?:Home[\s-]?Office|Remote|mobil\w*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex DaysBefore();

    /// <summary>„Homeoffice an 2 Tagen", „remote work 3 days a week".</summary>
    [GeneratedRegex(@"(?:Home[\s-]?Office|Remote|mobile[sn]?\s+Arbeit\w*)[^.\n]{0,25}?\b(?<n>[1-5]|ein(?:e[nm]?)?|zwei|drei|vier|one|two|three|four)\s+(?:Tage?n?|days?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex DaysAfter();
}
