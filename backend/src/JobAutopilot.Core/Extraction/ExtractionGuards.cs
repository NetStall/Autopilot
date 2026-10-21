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
/// <item>minimum years stay only when the text states a number of years;</item>
/// <item>a home-office share stays only when the text states one; „bis zu 3 Tage" read as 3 % becomes 60 %, stated
/// days count 20 % each (07b);</item>
/// <item>an item after „idealerweise" / „von Vorteil" inside a requirement line is a plus („DDD, CQRS und idealerweise
/// Event Sourcing");</item>
/// <item>a summary sentence with „zwingend", „erforderlich", „Pflicht" … is dropped when the posting never says so;</item>
/// <item>a German level stays only when the text mentions the language at all — a German posting alone asks for no
/// level (07c, MCA GROUP got „C1" from nothing).</item>
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
        var extraPlus = new List<string>();
        var mustHave = new List<string>();
        foreach (var item in extraction.MustHave)
        {
            if (!niceToHave.Contains(item) && IntroducedAsPlus(item, sentences))
            {
                extraPlus.Add(item);
            }
            else if (!niceToHave.Contains(item))
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
            NiceToHave = [.. extraction.NiceToHave.Where(niceToHave.Contains), .. extraPlus],
            DomainRequirements = domains,
            MinYears = extraction.MinYears is not null && StatedYears().IsMatch(description) ? extraction.MinYears : null,
            HomeOfficePercent = extraction.HomeOfficeDaysPerWeek is { } days and > 0 and <= 5 && HomeOfficeDays().IsMatch(description)
                ? days * 20
                : HomeOffice(extraction.HomeOfficePercent, description),
            Summary = SupportedSummary(extraction.Summary, description),
            GermanLevel = extraction.GermanLevel is not null && GermanMentioned().IsMatch(description) ? extraction.GermanLevel : null,
        };
    }

    /// <summary>„… DDD, CQRS und idealerweise Event Sourcing": a plus marker between the item and the last comma before it.</summary>
    private static bool IntroducedAsPlus(string item, IReadOnlyList<(int Start, string Text)> sentences)
    {
        foreach (var (_, sentence) in sentences)
        {
            var at = sentence.IndexOf(item, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            var before = sentence[..at];
            var clause = before[(Math.Max(before.LastIndexOf(',') , before.LastIndexOf(';')) + 1)..];
            return PlusMarker().IsMatch(clause);
        }

        return false;
    }

    /// <summary>Drops summary sentences that claim a strictness the posting never states.</summary>
    private static string SupportedSummary(string summary, string description)
    {
        var sentences = SummarySentence().Split(summary).Where(s => s.Trim().Length > 0).ToList();
        var kept = sentences.Where(s => StrongWord().Matches(s).All(m => description.Contains(m.Value, StringComparison.OrdinalIgnoreCase))).ToList();
        return kept.Count == sentences.Count ? summary : string.Join(" ", kept.Select(s => s.Trim()));
    }

    [GeneratedRegex(@"\b(?:idealerweise|wünschenswert|von\s+Vorteil|gerne|ideally|nice\s+to\s+have|ein\s+Plus)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex PlusMarker();

    [GeneratedRegex(@"\b(?:zwingend\w*|erforderlich\w*|Pflicht\w*|vorausgesetzt|Voraussetzung\w*|unabdingbar|mandatory|required)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex StrongWord();

    [GeneratedRegex(@"b(?:deutsch(?!land)\w*|german|DaF)b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex GermanMentioned();

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant, 250)]
    private static partial Regex SummarySentence();

    /// <summary>The model's share only when the text states one; a share of at most 5 next to „Tag(e)" is days (× 20).</summary>
    private static int? HomeOffice(int? percent, string description)
    {
        if (percent is not { } value)
        {
            return null;
        }

        if (value <= 5 && HomeOfficeDays().Match(description) is { Success: true } days
            && int.TryParse(days.Groups["n"].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n == value)
        {
            return Math.Min(n * 20, 100);
        }

        return HomeOfficeShare.Find(description) is { } stated ? stated : null;
    }

    [GeneratedRegex(@"\b(?<n>[1-5])\s+(?:Tage?n?|days?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex HomeOfficeDays();

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
