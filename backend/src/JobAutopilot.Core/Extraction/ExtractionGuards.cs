using System.Text.RegularExpressions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Core.Extraction;

/// <summary>
/// Checks a validated extraction against the posting's own text (step 07a2), so a model that over-reads cannot turn
/// everything into a must-have:
/// <list type="bullet">
/// <item>an item in both lists stays a must-have only when the text marks it as required (requirements section or a
/// marker like „Voraussetzung", „zwingend", „(Pflicht)" in its line); otherwise it is nice to have;</item>
/// <item>a domain stays only when a sentence that names it also asks for experience or knowledge — the company's
/// industry („Software für die digitale Patientenversorgung") is not a requirement;</item>
/// <item>minimum years stay only when the text states a number of years.</item>
/// </list>
/// </summary>
public static partial class ExtractionGuards
{
    private static readonly FitOptions Defaults = new();
    private static readonly Func<string, RequirementSections> Sections = RequirementSections.Parser(Defaults);
    private static readonly TermMatcher MustHaveMarkers = new(Defaults.EffectiveMustHaveMarkers);

    public static RequirementExtraction Apply(RequirementExtraction extraction, string description)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(description);
        var sentences = FitAnalyzer.Sentences(description);
        var sections = Sections(description);

        var niceToHave = new HashSet<string>(extraction.NiceToHave, StringComparer.OrdinalIgnoreCase);
        var mustHave = new List<string>();
        foreach (var item in extraction.MustHave)
        {
            if (!niceToHave.Contains(item))
            {
                mustHave.Add(item);
            }
            else if (MarkedAsRequired(item, description, sentences, sections))
            {
                mustHave.Add(item);
                niceToHave.Remove(item);
            }
        }

        var domains = extraction.DomainRequirements
            .Where(d => sentences.Any(s => NamesDomain(s.Text, d) && ExperienceWord().IsMatch(s.Text)))
            .ToList();

        return extraction with
        {
            MustHave = mustHave,
            NiceToHave = [.. extraction.NiceToHave.Where(niceToHave.Contains)],
            DomainRequirements = domains,
            MinYears = extraction.MinYears is not null && StatedYears().IsMatch(description) ? extraction.MinYears : null,
        };
    }

    private static bool MarkedAsRequired(string item, string description, IReadOnlyList<(int Start, string Text)> sentences, RequirementSections sections)
    {
        var matcher = new TermMatcher([item]);
        foreach (var (term, index) in matcher.FindAll(description))
        {
            _ = term;
            if (sections.At(index) == SectionKind.MustHave)
            {
                return true;
            }

            var sentence = sentences.LastOrDefault(s => s.Start <= index).Text ?? string.Empty;
            if (MustHaveMarkers.FindFirst(sentence) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>„Healthcare-Bereich" names „Healthcare"; „Broadcast/MAM" is named by „Broadcast" or „MAM".</summary>
    private static bool NamesDomain(string sentence, string domain) =>
        domain.Split(['/', ',', '(', ')', ' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 3)
            .Any(w => sentence.Contains(w, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\b(?:Erfahrung|Berufserfahrung|Kenntnisse|Know-how|Expertise|erforderlich|vorausgesetzt|Voraussetzung|zwingend|experience|required|knowledge)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex ExperienceWord();

    /// <summary>„3 Jahre", „5+ years", „mindestens fünf Jahre", „zwei Jahren".</summary>
    [GeneratedRegex(@"(?:\b\d{1,2}\s*\+?\s*(?:-\s*\d{1,2}\s*)?|\b(?:zwei|drei|vier|fünf|sechs|sieben|acht|zehn|two|three|four|five|six|seven|eight|ten)\s+)(?:Jahre|Jahren|years?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex StatedYears();
}
