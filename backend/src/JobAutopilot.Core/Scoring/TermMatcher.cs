using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobAutopilot.Core.Scoring;

/// <summary>
/// Finds configured terms as whole words. A term matches only when it is not glued to a letter or digit on either
/// side, so <c>Java</c> does not match „JavaScript", <c>.NET</c> not „Netzwerk" or „example.net", <c>Golang</c> not
/// „Google", but „Java-Entwickler" and „C#/.NET" do. Inside a term a space or hyphen matches any run of spaces and
/// hyphens, including none („Full Stack" = „Full-Stack" = „Fullstack"); <c>{n}</c> matches a number of 1–3 digits.
/// </summary>
public sealed class TermMatcher
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    private readonly (string Term, Regex Pattern)[] _terms;

    public TermMatcher(IEnumerable<string> terms, bool matchCase = false)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        _terms = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .Select(t => (t, new Regex(ToPattern(t), options, Timeout)))
            .ToArray();
    }

    /// <summary>The first configured term found in <paramref name="text"/> (in configuration order), or <c>null</c>.</summary>
    public string? FindFirst(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        foreach (var (term, pattern) in _terms)
        {
            if (pattern.IsMatch(text))
            {
                return term;
            }
        }

        return null;
    }

    /// <summary>All positions where a configured term occurs, with the term that matched.</summary>
    public IEnumerable<(string Term, int Index)> FindAll(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        foreach (var (term, pattern) in _terms)
        {
            foreach (Match match in pattern.Matches(text))
            {
                yield return (term, match.Index);
            }
        }
    }

    internal static string ToPattern(string term)
    {
        var pattern = new StringBuilder(@"(?<![\p{L}\p{N}_])");
        var separatorPending = false;
        for (var i = 0; i < term.Length; i++)
        {
            var c = term[i];
            if (char.IsWhiteSpace(c) || c == '-')
            {
                separatorPending = true;
                continue;
            }

            if (separatorPending)
            {
                pattern.Append(@"[\s\-]*");
                separatorPending = false;
            }

            if (term.AsSpan(i).StartsWith("{n}", StringComparison.Ordinal))
            {
                pattern.Append(@"\d{1,3}");
                i += 2;
                continue;
            }

            pattern.Append(Regex.Escape(c.ToString(CultureInfo.InvariantCulture)));
        }

        // "#" and "+" belong to a word here: "C" must not match "C#" or "C++".
        return pattern.Append(@"(?![\p{L}\p{N}_#+])").ToString();
    }
}
