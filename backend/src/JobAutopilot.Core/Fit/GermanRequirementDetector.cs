using System.Text.RegularExpressions;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Fit;

/// <summary>What a posting says about German, with the phrase that decided it (for the reasons).</summary>
public sealed record GermanRequirement(RequiredGerman Category, LanguageLevel? Level, string? Phrase)
{
    public static readonly GermanRequirement Unknown = new(RequiredGerman.Unknown, null, null);

    /// <summary>„Deutsch: B2 („sicher auf Deutsch“)" for the reasons; <c>null</c> when nothing was found.</summary>
    public string? Reason => Category switch
    {
        RequiredGerman.Unknown => null,
        RequiredGerman.None => Phrase is null ? "Deutsch: nicht gefordert (englische Anzeige)" : $"Deutsch: nicht gefordert („{Phrase}“)",
        _ => $"Deutsch: {Label(Category)} („{Phrase}“)",
    };

    public static string Label(RequiredGerman category) => category switch
    {
        RequiredGerman.C1Plus => "C1 oder höher",
        RequiredGerman.B2 => "B2",
        RequiredGerman.B1OrLower => "B1 oder niedriger",
        RequiredGerman.None => "Kein Deutsch nötig",
        _ => "Unbekannt",
    };
}

/// <summary>
/// Detects the German level a posting requires: CEFR levels next to „Deutsch"/„German", and phrases —
/// C1+: „verhandlungssicher", „sehr gute Deutschkenntnisse (in Wort und Schrift)", „fließend Deutsch", „Muttersprache";
/// B2: „gute Deutschkenntnisse", „sicher auf Deutsch", „sicheres Deutsch"; B1 or lower: A1–B1, „Grundkenntnisse",
/// „Deutschkenntnisse von Vorteil / wünschenswert". The strongest finding wins. <see cref="RequiredGerman.None"/> only
/// without any requirement: „German is not required", „English is our working language", or an English posting.
/// </summary>
public static partial class GermanRequirementDetector
{
    public static GermanRequirement Detect(string title, string? description)
    {
        ArgumentNullException.ThrowIfNull(title);
        var text = string.IsNullOrWhiteSpace(description) ? title : $"{title}\n{description}";
        GermanRequirement? best = null;

        // „Deutsch … B2" and „C1 … Deutsch" separately, so one match cannot hide the other („Englisch C1, Deutsch B2").
        foreach (var match in CefrAfterGerman().Matches(text).Concat(CefrBeforeGerman().Matches(text)))
        {
            if (match.Groups["b"].Success && BelongsToOtherLanguage(text, match.Index))
            {
                continue;
            }

            var token = (match.Groups["a"].Success ? match.Groups["a"].Value : match.Groups["b"].Value).ToUpperInvariant();
            var level = Enum.Parse<LanguageLevel>(token);
            Consider(level >= LanguageLevel.C1 ? RequiredGerman.C1Plus : level == LanguageLevel.B2 ? RequiredGerman.B2 : RequiredGerman.B1OrLower, level, match.Value);
        }

        foreach (Match match in C1Phrases().Matches(text))
        {
            if (!BelongsToOtherLanguage(text, match.Index))
            {
                Consider(RequiredGerman.C1Plus, LanguageLevel.C1, match.Value);
            }
        }

        foreach (Match match in B2Phrases().Matches(text))
        {
            if (!BelongsToOtherLanguage(text, match.Index))
            {
                Consider(RequiredGerman.B2, LanguageLevel.B2, match.Value);
            }
        }

        foreach (Match match in BasicPhrases().Matches(text))
        {
            Consider(RequiredGerman.B1OrLower, LanguageLevel.A2, match.Value);
        }

        if (best is not null)
        {
            return best;
        }

        if (NotRequired().Match(text) is { Success: true } none)
        {
            return new GermanRequirement(RequiredGerman.None, null, Clean(none.Value));
        }

        return string.IsNullOrWhiteSpace(description) || ToneDetector.DetectLanguage(text) != DocumentLanguage.En
            ? GermanRequirement.Unknown
            : new GermanRequirement(RequiredGerman.None, null, null);

        void Consider(RequiredGerman category, LanguageLevel level, string phrase)
        {
            if (best is null || category > best.Category)
            {
                best = new GermanRequirement(category, level, Clean(phrase));
            }
        }
    }

    /// <summary>One line, without trailing punctuation; an opening bracket cut off by the match is closed again.</summary>
    /// <summary>„Englisch C1, …", „Englisch verhandlungssicher": a level or quality right after another language is about that one.</summary>
    private static bool BelongsToOtherLanguage(string text, int index)
    {
        var start = Math.Max(0, index - 20);
        return OtherLanguageBefore().IsMatch(text[start..index]);
    }

    private static string Clean(string phrase)
    {
        var text = WhiteSpace().Replace(phrase, " ").Trim().Trim(',', ';', ':', '.');
        return text.Count(c => c == '(') > text.Count(c => c == ')') ? text + ")" : text;
    }

    /// <summary>„Deutschkenntnisse", „Deutschsprachkenntnissen" — the noun a quality after it can only describe.</summary>
    private const string KnowledgeNoun = @"(?:deutsch(?:sprach)?kenntnissen?)";

    private const string GermanWord = @"(?:deutsch(?:e|en)?(?:kenntnisse|kenntnissen|sprachkenntnisse|sprachkenntnissen|sprachig|sprache)?|german)";

    /// <summary>Between the language word and the level no other language may appear („Englisch C1, Deutsch B2").</summary>
    private const string NoOtherLanguage = @"(?:(?!englisch|english|französisch|french|spanisch|spanish|italienisch|polnisch)[^.\n]){0,40}?";

    [GeneratedRegex(@"\b" + GermanWord + @"\b" + NoOtherLanguage + @"\b(?<a>[ABC][12])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex CefrAfterGerman();

    [GeneratedRegex(@"\b(?<b>[ABC][12])\b" + NoOtherLanguage + @"\b" + GermanWord + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex CefrBeforeGerman();

    [GeneratedRegex(@"(?:englisch|english|französisch|french|spanisch|spanish|italienisch|polnisch)\w*[\s,:(-]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex OtherLanguageBefore();

    [GeneratedRegex(
        @"\bverhandlungssicher\w*[^.\n]{0,30}?\b" + GermanWord + @"\b" +
        @"|\b" + GermanWord + @"\b[^.\n]{0,40}?\bverhandlungssicher\w*" +
        @"|\b(?:sehr\s+gut\w*|exzellent\w*|hervorragend\w*|ausgezeichnet\w*|perfekt\w*)\s+(?:\w+\s+){0,2}?" + GermanWord + @"\b(?:\s+in\s+Wort\s+und\s+Schrift)?" +
        @"|\b" + GermanWord + @"\b\s+(?:\w+\s+){0,3}?(?:sehr\s+gut|fließend|fliessend|auf\s+muttersprach\w*|muttersprachlich\w*|als\s+Muttersprache)" +
        // „Deine Deutschkenntnisse in Wort und Schrift sind sehr gut" (Lindenhof, 07b): only after the noun Kenntnisse,
        // so „deutschen Kunden hervorragend betreuen" stays out.
        @"|\b" + KnowledgeNoun + @"\b\s+(?:\w+\s+){0,6}?(?:sehr\s+gut|exzellent|hervorragend|ausgezeichnet|fließend|fliessend|verhandlungssicher)" +
        @"|\b(?:fließend\w*|fliessend\w*)\s+(?:\w+\s+){0,2}?" + GermanWord + @"\b" +
        @"|\b(?:Muttersprache|muttersprachlich\w*)\s+(?:\w+\s+){0,1}?" + GermanWord + @"\b" +
        @"|\bdeutsche\s+Muttersprache" +
        @"|\b(?:fluent|business[\s-]fluent|excellent|native)\s+(?:\w+\s+)?german\b|\bgerman\s*\(?\s*(?:fluent|native|C1|C2)\b|\bgerman\s+(?:at\s+)?(?:native|business)\s+level",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex C1Phrases();

    [GeneratedRegex(
        // „sehr gute Deutschkenntnisse" also matches here, but its C1 match wins (the strongest finding counts).
        @"\bgute\w*\s+(?:\w+\s+){0,2}?" + GermanWord + @"\b" +
        @"|\bsicher\w*\s+(?:auf|in)\s+Deutsch\b|\bsicheres?\s+Deutsch\b" +
        // „Deine Deutschkenntnisse sind gut", „Deutsch in Wort und Schrift" (without „sehr gut" — that is C1 above).
        @"|\b" + KnowledgeNoun + @"\b\s+(?:\w+\s+){0,4}?(?:gut|sicher)\b" +
        @"|\bDeutsch(?:kenntnisse)?\s+in\s+Wort\s+und\s+Schrift" +
        @"|\bgood\s+(?:\w+\s+)?german\b|\bsolid\s+german\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex B2Phrases();

    [GeneratedRegex(
        @"\bGrundkenntnisse\s+(?:in\s+|der\s+)?(?:deutsch\w*)\b|\bdeutsch\w*[\s-]+Grundkenntnisse\b|\bdeutsche\s+Grundkenntnisse\b" +
        @"|\b" + GermanWord + @"\b[^.\n]{0,25}?\b(?:von\s+Vorteil|wünschenswert|ein\s+Plus|vorteilhaft|nice\s+to\s+have)" +
        @"|\b(?:basic|some)\s+german\b|\bgerman\b[^.\n]{0,25}?\b(?:is\s+a\s+plus|a\s+plus|nice\s+to\s+have|beneficial|an\s+advantage|is\s+an\s+asset)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex BasicPhrases();

    [GeneratedRegex(
        @"\bgerman\b[^.\n]{0,30}?\b(?:is\s+)?not\s+(?:required|needed|necessary|a\s+requirement)\b|\bno\s+german\b[^.\n]{0,20}?(?:required|needed|necessary)|\bwithout\s+(?:any\s+)?german\b" +
        @"|\benglish\s+is\s+(?:our|the)\s+(?:\w+\s+)?(?:working|company|team|official)\s+language\b|\b(?:our\s+)?working\s+language\s+is\s+english\b" +
        @"|\bkeine\s+Deutschkenntnisse\s+(?:nötig|notwendig|erforderlich)\b|\bDeutschkenntnisse\s+(?:sind\s+)?nicht\s+(?:nötig|notwendig|erforderlich)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex NotRequired();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 250)]
    private static partial Regex WhiteSpace();
}
