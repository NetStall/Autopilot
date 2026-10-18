using System.Text.RegularExpressions;

namespace JobAutopilot.Core.Import;

/// <summary>
/// Coarse pre-filter for obvious noise (apprenticeships, internships, working students …): a title is excluded when
/// it contains one of the terms as a whole word or phrase, case-insensitively. "Ausbildung" matches
/// "Ausbildung zum Fachinformatiker" and "Ausbildung/Studium", but not "Ausbildungsleiter".
/// Real relevance scoring is a later step; this only keeps clearly irrelevant postings out of the database.
/// </summary>
public sealed class TitleExclusionFilter
{
    public static readonly TitleExclusionFilter None = new([]);

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private readonly Regex? _pattern;

    public TitleExclusionFilter(IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var alternatives = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(ToPattern)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (alternatives.Count > 0)
        {
            // Not preceded / followed by a letter or digit = whole word, also next to "/", "-", "(", "*" etc.
            _pattern = new Regex(
                $@"(?<![\p{{L}}\p{{N}}])(?:{string.Join('|', alternatives)})(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                MatchTimeout);
        }
    }

    public bool IsExcluded(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return _pattern?.IsMatch(title) ?? false;
    }

    /// <summary>"Duales Studium" also matches "Duales  Studium" (any whitespace between the words).</summary>
    private static string ToPattern(string term) =>
        string.Join(@"\s+", term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
}
