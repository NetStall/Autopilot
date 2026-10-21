using System.Text.RegularExpressions;

namespace JobAutopilot.Core.Extraction;

/// <summary>
/// Alternatives in the posting's own words (08a): items the model listed one by one become one entry „A oder B" when
/// the text joins them — „Angular oder Vue.js", „Angular/Vue", „bzw." — or names them as examples of a category:
/// „relationale Datenbanken (z. B. MSSQL, PostgreSQL, MySQL)" is one requirement, met by any of them. The normaliser
/// then treats an entry with one covered skill as fulfilled (the others are alternatives).
/// </summary>
public static partial class ExtractionAlternatives
{
    public static IReadOnlyList<string> Merge(IReadOnlyList<string> items, IEnumerable<string> sentences)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sentences);
        if (items.Count < 2)
        {
            return items;
        }

        var parent = Enumerable.Range(0, items.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        void Union(int a, int b) => parent[Find(b)] = Find(a);

        foreach (var raw in sentences)
        {
            var sentence = Markup().Replace(raw, string.Empty);
            var found = items
                .Select((item, i) => (Index: i, Span: Position(sentence, item)))
                .Where(f => f.Span is not null)
                .Select(f => (f.Index, Start: f.Span!.Value.Start, End: f.Span.Value.End))
                .OrderBy(f => f.Start)
                .ToList();

            // „A oder B", „A/B", „A bzw. B".
            for (var k = 0; k + 1 < found.Count; k++)
            {
                if (found[k + 1].Start >= found[k].End && OrJoiner().IsMatch(sentence[found[k].End..found[k + 1].Start]))
                {
                    Union(found[k].Index, found[k + 1].Index);
                }
            }

            // „Kategorie (z. B. X, Y, Z)" / „Frameworks wie X, Y oder Z".
            foreach (Match marker in ExampleMarker().Matches(sentence))
            {
                var listEnd = sentence.IndexOfAny([')', ';', ':'], marker.Index + marker.Length);
                listEnd = listEnd < 0 ? sentence.Length : listEnd;
                var examples = found.Where(f => f.Start >= marker.Index + marker.Length && f.End <= listEnd).ToList();
                // „wie C#, .NET und Vue" lists several requirements; only „z. B." always means examples.
                var strict = marker.Groups["strict"].Success;
                if (examples.Count == 0 || (!strict && examples.Count > 1 && AndJoiner().IsMatch(sentence[examples[0].Start..examples[^1].End])))
                {
                    continue;
                }

                // The category right before the marker („relationalen Datenbanken (z. B."), if the model listed it too.
                var categories = found.Where(f => f.End <= marker.Index && sentence[f.End..marker.Index].Trim().Trim('(').Trim().Length == 0).ToList();
                var anchor = categories.Count > 0 ? categories[^1].Index : examples[0].Index;
                foreach (var example in examples)
                {
                    Union(anchor, example.Index);
                }
            }
        }

        return [.. Enumerable.Range(0, items.Count)
            .GroupBy(Find)
            .OrderBy(g => g.Min())
            .Select(g => string.Join(" oder ", g.OrderBy(i => i).Select(i => items[i])))];
    }

    /// <summary>Where an item occurs: the exact text, or for a phrase the stems of its words („relationale Datenbanken" in „relationalen Datenbanken").</summary>
    private static (int Start, int End)? Position(string sentence, string item)
    {
        var at = sentence.IndexOf(item, StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            return (at, at + item.Length);
        }

        var words = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words.Any(w => w.Length < 4))
        {
            return null;
        }

        var start = -1;
        var end = 0;
        foreach (var word in words)
        {
            var stem = word[..Math.Max(4, word.Length - 2)];
            var found = sentence.IndexOf(stem, end, StringComparison.OrdinalIgnoreCase);
            if (found < 0 || (start >= 0 && found - end > 3))
            {
                return null;
            }

            start = start < 0 ? found : start;
            end = found + stem.Length;
            while (end < sentence.Length && char.IsLetter(sentence[end]))
            {
                end++;
            }
        }

        return (start, end);
    }

    [GeneratedRegex(@"\*+|`", RegexOptions.CultureInvariant, 250)]
    private static partial Regex Markup();

    [GeneratedRegex(@"^\s*,?\s*(?:oder|bzw\.?|or|/)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex OrJoiner();

    [GeneratedRegex(@"\b(?:und|sowie|and)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex AndJoiner();

    [GeneratedRegex(@"(?<strict>\bz\.\s?B\.|\bzum Beispiel\b|\bbeispielsweise\b|\bbspw\.|\be\.\s?g\.|\bsuch as\b)|\b(?:wie|etwa|like)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex ExampleMarker();
}
