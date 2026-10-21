using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Core.Documents;

/// <summary>
/// Checks a letter draft against the facts it may use — automatically after every generation and every save. It
/// warns when a technology sounds like experience but is not in the profile (or only <c>Learning</c>/<c>None</c>
/// without an honest phrasing), when a number or duration appears nowhere in the profile or the posting, when a
/// required gap is not mentioned, when „Sie" and „du" are mixed, when the language or tone does not match the posting,
/// and when the letter is longer than one page. Style hints: website missing in the first paragraph, clichés.
/// Rule-based; it cannot prove a letter right, it points the review at likely problems.
/// </summary>
public static partial class FactGuard
{
    /// <summary>Above this many characters (paragraphs, subject, salutation) a letter likely needs a second page.</summary>
    public const int MaxLetterCharacters = 2600;

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zwei"] = 2, ["drei"] = 3, ["vier"] = 4, ["fünf"] = 5, ["sechs"] = 6, ["sieben"] = 7, ["acht"] = 8, ["neun"] = 9,
        ["zehn"] = 10, ["elf"] = 11, ["zwölf"] = 12, ["zwanzig"] = 20, ["hundert"] = 100, ["tausend"] = 1000,
        ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["twenty"] = 20, ["hundred"] = 100, ["thousand"] = 1000,
    };

    /// <summary>Enums as names: their numeric values must not count as „numbers in the profile".</summary>
    private static readonly JsonSerializerOptions ProfileJson = new() { Converters = { new JsonStringEnumConverter() } };

    private static readonly string[] Cliches =
    [
        "hiermit bewerbe ich mich", "teamfähig und belastbar", "belastbar und teamfähig", "mit großem Interesse habe ich",
        "hochmotiviert", "I am writing to apply", "team player", "I am excited to apply",
    ];

    /// <param name="knownTechnologies">
    /// Technology names the guard looks for besides the profile's skills, e.g. the fit check's common requirements
    /// (Angular, Kubernetes, React …).
    /// </param>
    /// <param name="renderedPages">Pages of the rendered PDF if known; otherwise a character estimate decides.</param>
    public static IReadOnlyList<DraftWarning> Check(
        LetterDraft letter,
        LetterInput input,
        CandidateProfile profile,
        IEnumerable<string> knownTechnologies,
        int? renderedPages = null)
    {
        ArgumentNullException.ThrowIfNull(letter);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(knownTechnologies);

        var warnings = new List<DraftWarning>();
        CheckTechnologies(letter, input, profile, knownTechnologies, warnings);
        CheckLanguages(letter, profile, warnings);
        CheckNumbers(letter, input, profile, warnings);
        CheckGaps(letter, input, profile, warnings);
        CheckTone(letter, input, warnings);
        CheckLength(letter, renderedPages, warnings);
        CheckStyle(letter, input, warnings);
        return warnings;
    }

    private static void CheckTechnologies(
        LetterDraft letter, LetterInput input, CandidateProfile profile, IEnumerable<string> known, List<DraftWarning> warnings)
    {
        // A technology named in one of my experience, achievement or project facts („von jQuery zu Vue migriert") is a
        // fact, even when it is no skill of its own.
        var factText = string.Join("\n", input.Facts
            .Where(f => f.Kind is LetterFactKind.Experience or LetterFactKind.Achievement or LetterFactKind.Project or LetterFactKind.Summary)
            .Select(f => f.Text));

        var skillsByName = new Dictionary<string, Skill>(StringComparer.Ordinal);
        foreach (var skill in profile.Skills)
        {
            foreach (var name in skill.AllNames())
            {
                skillsByName.TryAdd(VacancyTerms.Normalize(name), skill);
            }
        }

        var names = profile.Skills.SelectMany(s => s.AllNames())
            .Concat(profile.KnownGaps.SelectMany(g => g.Aliases.Prepend(g.Skill)))
            .Concat(known)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !LanguageName().IsMatch(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Short names (Go, R, C, SAP …) only with their exact case, the rest case-insensitive.
        var matchers = new[]
        {
            new TermMatcher(names.Where(n => n.Trim().Length <= 3), matchCase: true),
            new TermMatcher(names.Where(n => n.Trim().Length > 3)),
        };

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (paragraph, index) in letter.Paragraphs.Select((p, i) => (p, i)))
        {
            // A technology named once in an honest sentence („Ganz offen: Mit Angular habe ich noch nicht …") stays honest
            // for the rest of the paragraph („Da ihr Angular oder Vue.js sucht …", 06c).
            var honestTerms = Sentences(paragraph).Where(s => HonestMarker().IsMatch(s))
                .SelectMany(s => matchers.SelectMany(m => m.FindAll(s)))
                .Select(f => VacancyTerms.Normalize(f.Term))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var sentence in Sentences(paragraph))
            {
                var honest = HonestMarker().IsMatch(sentence);
                var theirs = EmployerReference().IsMatch(sentence);
                foreach (var term in matchers.SelectMany(m => m.FindAll(sentence)).Select(f => f.Term).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var key = VacancyTerms.Normalize(term);
                    skillsByName.TryGetValue(key, out var skill);
                    if (honest || honestTerms.Contains(key) || !reported.Add(key))
                    {
                        continue;
                    }

                    if (skill is null && new TermMatcher([term]).FindFirst(factText) is not null)
                    {
                        continue;
                    }

                    if (skill is null || skill.Level == SkillLevel.None || profile.KnownGaps.Any(g => LetterFactsBuilder.SameSkill(g, term)))
                    {
                        warnings.Add(new(DraftWarning.InventedTechnology,
                            $"„{term}“ klingt nach Erfahrung, steht im Profil aber nicht (oder als Lücke). Ehrlich als Lücke formulieren oder streichen.",
                            index,
                            theirs ? WarningSeverity.Hint : WarningSeverity.Warning));
                    }
                    else if (skill.Level == SkillLevel.Learning)
                    {
                        warnings.Add(new(DraftWarning.LearningAsExperience,
                            $"„{term}“ steht im Profil nur als „Learning“ – nicht als Erfahrung darstellen.",
                            index,
                            theirs ? WarningSeverity.Hint : WarningSeverity.Warning));
                    }
                    else
                    {
                        reported.Remove(key);
                    }
                }
            }
        }
    }

    /// <summary>
    /// „Mein Deutsch liegt auf B1": a language with a level is checked against the profile's language levels — the same
    /// or a lower level is fine, a higher one is a warning (06c). Languages are never technologies.
    /// </summary>
    private static void CheckLanguages(LetterDraft letter, CandidateProfile profile, List<DraftWarning> warnings)
    {
        foreach (var (paragraph, index) in letter.Paragraphs.Select((p, i) => (p, i)))
        {
            foreach (Match match in LanguageLevelPattern().Matches(paragraph))
            {
                var language = match.Groups["lang"].Value;
                var german = language.StartsWith("deutsch", StringComparison.OrdinalIgnoreCase) || language.StartsWith("german", StringComparison.OrdinalIgnoreCase);
                var mine = profile.Languages.FirstOrDefault(l => SameLanguage(l, german));
                if (!Enum.TryParse<LanguageLevel>(match.Groups["level"].Value, ignoreCase: true, out var claimed))
                {
                    continue;
                }

                if (mine is null || claimed > mine.Level)
                {
                    warnings.Add(new(DraftWarning.LanguageAboveProfile,
                        $"„{match.Value.Trim()}“ – im Profil steht {(mine is null ? "diese Sprache nicht" : $"{language.Trim()} {mine.Level}")}.", index));
                }
            }
        }
    }

    private static bool SameLanguage(LanguageSkill language, bool german)
    {
        var names = new[] { language.Name, language.NameDe ?? string.Empty };
        return german
            ? names.Any(n => n.Trim() is "Deutsch" or "German")
            : names.Any(n => n.Trim() is "Englisch" or "English");
    }

    private static void CheckNumbers(LetterDraft letter, LetterInput input, CandidateProfile profile, List<DraftWarning> warnings)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "1" };
        AddNumbers(allowed, JsonSerializer.Serialize(profile, ProfileJson));
        foreach (var fact in input.Facts)
        {
            AddNumbers(allowed, fact.Text);
        }

        AddNumbers(allowed, $"{input.Vacancy.Title} {input.Vacancy.Description} {input.Vacancy.Location}");
        AddNumbers(allowed, input.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (paragraph, index) in letter.Paragraphs.Select((p, i) => (p, i)))
        {
            foreach (var (shown, value) in Numbers(paragraph))
            {
                if (!allowed.Contains(value) && reported.Add(value))
                {
                    warnings.Add(new(DraftWarning.InventedNumber,
                        $"Die Angabe „{shown}“ steht weder im Profil noch in der Stellenanzeige.", index));
                }
            }
        }
    }

    private static void CheckGaps(LetterDraft letter, LetterInput input, CandidateProfile profile, List<DraftWarning> warnings)
    {
        var text = string.Join("\n", letter.Paragraphs);
        foreach (var gap in input.Fit.Requirements.Where(r => r.Coverage == FitCoverage.Gap && !r.Optional))
        {
            var names = profile.KnownGaps.FirstOrDefault(g => LetterFactsBuilder.SameSkill(g, gap.Name)) is { } known
                ? known.Aliases.Prepend(known.Skill).Append(gap.Name)
                : [gap.Name];
            if (new TermMatcher(names).FindFirst(text) is null)
            {
                warnings.Add(new(DraftWarning.MissingGap,
                    $"Die Stelle fordert „{gap.Name}“ – das fehlt im Profil und wird im Anschreiben nicht offen angesprochen."));
            }
        }
    }

    private static void CheckTone(LetterDraft letter, LetterInput input, List<DraftWarning> warnings)
    {
        var body = string.Join("\n", letter.Paragraphs.Append(letter.Closing));
        var language = ToneDetector.DetectLanguage(body);
        if (language != input.Tone.Language)
        {
            warnings.Add(new(DraftWarning.LanguageMismatch,
                $"Das Anschreiben ist auf {Name(language)}, die Stellenanzeige auf {Name(input.Tone.Language)}."));
        }

        if (language != DocumentLanguage.De)
        {
            return;
        }

        var du = LetterDuPattern().Count(body);
        var sie = LetterSiePattern().Count(body);
        if (du > 0 && sie > 0)
        {
            warnings.Add(new(DraftWarning.MixedFormality, $"„Sie“ und „du“ sind gemischt ({sie}× Sie-Form, {du}× du-Form)."));
        }
        else
        {
            var used = du > 0 ? Formality.Du : sie > 0 ? Formality.Sie : letter.Formality;
            if (used != input.Tone.Formality)
            {
                warnings.Add(new(DraftWarning.FormalityMismatch,
                    $"Die Anzeige {(input.Tone.Formality == Formality.Du ? "duzt" : "siezt")}, das Anschreiben {(used == Formality.Du ? "duzt" : "siezt")}.",
                    Severity: WarningSeverity.Hint));
            }
        }
    }

    private static void CheckLength(LetterDraft letter, int? renderedPages, List<DraftWarning> warnings)
    {
        var characters = letter.AllText().Sum(t => t.Length);
        if (renderedPages is > 1 || (renderedPages is null && characters > MaxLetterCharacters))
        {
            warnings.Add(new(DraftWarning.TooLong, renderedPages is > 1
                ? $"Das Anschreiben passt nicht auf eine Seite ({renderedPages} Seiten)."
                : $"Das Anschreiben ist wahrscheinlich länger als eine Seite ({characters} Zeichen)."));
        }
    }

    private static void CheckStyle(LetterDraft letter, LetterInput input, List<DraftWarning> warnings)
    {
        if (input.Website is { } website && letter.Paragraphs.Count > 0
            && !letter.Paragraphs[0].Contains(Links.Display(website), StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new(DraftWarning.MissingWebsite,
                $"Die Website {Links.Display(website)} fehlt im ersten Absatz.", 0, WarningSeverity.Hint));
        }

        foreach (var (paragraph, index) in letter.Paragraphs.Select((p, i) => (p, i)))
        {
            foreach (var cliche in Cliches.Where(c => paragraph.Contains(c, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(new(DraftWarning.Cliche, $"Floskel „{cliche}“ – lieber konkret werden.", index, WarningSeverity.Hint));
            }
        }
    }

    private static string Name(DocumentLanguage language) => language == DocumentLanguage.De ? "Deutsch" : "Englisch";

    public static IEnumerable<string> Sentences(string paragraph) =>
        SentenceBreak().Split(paragraph).Where(s => !string.IsNullOrWhiteSpace(s));

    /// <summary>Numbers as shown and normalized: „1.000" → <c>1000</c>, „8,7" → <c>8.7</c>, „drei" → <c>3</c>.</summary>
    public static IEnumerable<(string Shown, string Value)> Numbers(string text)
    {
        foreach (Match match in NumberPattern().Matches(text))
        {
            var raw = match.Groups["n"].Value;
            if (NumberWords.TryGetValue(raw, out var word))
            {
                yield return (match.Value.Trim(), word.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            yield return (match.Value.Trim(), NormalizeNumber(raw));
        }
    }

    private static void AddNumbers(HashSet<string> allowed, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var (_, value) in Numbers(text))
        {
            allowed.Add(value);
        }

        // Dates and ranges also allow their parts („2022-07" → 2022, 7).
        foreach (Match digits in DigitsPattern().Matches(text))
        {
            allowed.Add(NormalizeNumber(digits.Value));
        }
    }

    private static string NormalizeNumber(string raw)
    {
        // Thousands separators: „1.000", „1,000" — a group of exactly three digits after the separator.
        var value = ThousandsPattern().Replace(raw, "$1");
        value = value.Replace(',', '.');
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.##", CultureInfo.InvariantCulture)
            : value;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant, 250)]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"(?<![\p{L}\d])(?<n>\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?|zwei|drei|vier|fünf|sechs|sieben|acht|neun|zehn|elf|zwölf|zwanzig|hundert|tausend|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|twenty|hundred|thousand)(?![\p{L}\d])(?:\s*(?:%|Prozent|percent|Jahre[n]?|Monate[n]?|years?|months?))?",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^(?:Deutsch|Englisch|German|English|Französisch|French|Spanisch|Spanish|Russisch|Russian|Ukrainisch|Ukrainian|Polnisch|Polish)\w*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex LanguageName();

    /// <summary>„Deutsch auf B1", „Mein Deutsch liegt auf B1", „Englisch (C1)", „English at B2" — a level within a few words.</summary>
    [GeneratedRegex(@"(?<lang>\b(?:Deutsch|Englisch|German|English)\w*)(?:\W+\w+){0,4}?\W+(?<level>[ABC][12])\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex LanguageLevelPattern();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant, 250)]
    private static partial Regex DigitsPattern();

    [GeneratedRegex(@"(?<=\d)[.,](\d{3})(?!\d)", RegexOptions.CultureInvariant, 250)]
    private static partial Regex ThousandsPattern();

    /// <summary>A sentence that is honest about a gap or about learning („Ganz offen", „noch nicht", „not yet").</summary>
    [GeneratedRegex(@"ganz offen|offen gesagt|ehrlich|noch nicht|noch keine|keine (praktische |berufliche |produktive )?Erfahrung|bisher (nur|keine|nicht)|nicht produktiv|nur in der Theorie|lerne|einarbeiten|einzuarbeiten|kenne ich (bisher )?nur|Lücke|not yet|no (hands-on |professional |production )?experience|haven't|have not|only in theory|learning|to be honest|honestly",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex HonestMarker();

    /// <summary>The sentence talks about the employer's stack („Ihr Stack mit Angular"), not about my experience.</summary>
    [GeneratedRegex(@"\b(Ihr|Ihre|Ihrem|Ihren|Ihrer|ihr \w+t|euer|eure|euch|dein|deine|bei Ihnen|bei euch|your|you use|you are using)\b",
        RegexOptions.CultureInvariant, 250)]
    private static partial Regex EmployerReference();

    [GeneratedRegex(@"\b(du|dich|dir|dein|deine|deinen|deinem|deiner|deines|euch|euer|eure|euren|eurem)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex LetterDuPattern();

    [GeneratedRegex(@"\b(Sie|Ihnen|Ihr|Ihre|Ihren|Ihrem|Ihrer|Ihres)\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex LetterSiePattern();
}
