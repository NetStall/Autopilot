using System.Globalization;
using System.Text.RegularExpressions;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Fit;

public enum FitCoverage
{
    /// <summary>Used professionally (profile level Production).</summary>
    Covered,

    /// <summary>Own projects or learning.</summary>
    Partial,

    /// <summary>Not in the profile, level None, or a known gap.</summary>
    Gap,
}

/// <summary>One requirement found in the posting. <see cref="Note"/>: level, or the honest phrasing for a gap.</summary>
public sealed record FitRequirement(string Name, FitCoverage Coverage, bool Optional, string Note);

public sealed record FitWarning(string Text, int Penalty);

/// <summary>What the fit check looks at; location and remote level decide the region warning.</summary>
public sealed record FitInput(string Title, string? Description, EmployerType EmployerType, string? Location, RemoteLevel RemoteLevel);

/// <summary>Result of <see cref="FitAnalyzer.Analyze"/>; <see cref="Penalty"/> is subtracted for the ranking.</summary>
public sealed record FitResult(
    IReadOnlyList<FitRequirement> Requirements,
    IReadOnlyList<FitWarning> Warnings,
    LanguageLevel? RequiredGerman,
    int? RequiredYears,
    int Penalty)
{
    public static readonly FitResult Empty = new([], [], null, null, 0);
}

/// <summary>
/// Rule-based fit check of one vacancy against the profile — no AI. Requirements are the profile's skills (by name
/// and aliases) and the configured common requirements found in title and description; each is covered, partial or
/// a gap (with the honest phrasing from <c>knownGaps</c>). A requirement in a sentence with „von Vorteil" & co. is
/// optional. German level and years of experience are compared with the profile and give warnings, never an
/// exclusion. Pure and deterministic.
/// </summary>
public sealed partial class FitAnalyzer(FitOptions options)
{
    private readonly (FitRequirementRule Rule, TermMatcher Matcher)[] _common =
        [.. options.CommonRequirements.Select(r => (r, new TermMatcher(r.Terms.Append(r.Name))))];

    private readonly TermMatcher _optionalMarkers = new(options.OptionalMarkers);

    public FitResult Analyze(CandidateProfile profile, string title, string? description, EmployerType employerType, DateOnly today) =>
        Analyze(profile, new FitInput(title, description, employerType, null, RemoteLevel.Unknown), today, FitSettings.Neutral);

    /// <param name="settings">From the Suchprofil: penalties, my region, remote wish, employer types ranked lower.</param>
    public FitResult Analyze(CandidateProfile profile, FitInput input, DateOnly today, FitSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        var (title, description, employerType, location, remoteLevel) = input;
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(title);
        var text = string.IsNullOrWhiteSpace(description) ? title : title + "\n" + description;
        var sentences = Sentences(text);
        var requirements = new List<FitRequirement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var covered = new TermMatcher(profile.Skills.Where(s => s.Level == SkillLevel.Production).SelectMany(s => s.AllNames()));

        foreach (var skill in profile.Skills.Where(s => !string.IsNullOrWhiteSpace(s.Name)))
        {
            var matcher = new TermMatcher(skill.AllNames());
            var hits = matcher.FindAll(text).ToList();
            if (hits.Count == 0 || !seen.Add(skill.Name))
            {
                continue;
            }

            foreach (var name in skill.AllNames())
            {
                seen.Add(name);
            }

            var gap = GapFor(profile, skill.AllNames());
            var (coverage, note) = skill.Level switch
            {
                SkillLevel.Production => (FitCoverage.Covered, "beruflich" + (skill.Years is { } y ? $", {y.ToString(CultureInfo.InvariantCulture)} J." : string.Empty)),
                SkillLevel.Project => (FitCoverage.Partial, gap?.De ?? "eigene Projekte"),
                SkillLevel.Learning => (FitCoverage.Partial, gap?.De ?? "lerne ich gerade"),
                _ => (FitCoverage.Gap, gap?.De ?? "keine Erfahrung"),
            };
            requirements.Add(new FitRequirement(skill.Name, coverage, AllSoft(hits, sentences, coverage == FitCoverage.Gap ? covered : null), note));
        }

        var warnings = new List<FitWarning>();
        foreach (var (rule, matcher) in _common)
        {
            var hits = matcher.FindAll(text).ToList();
            if (hits.Count == 0 || seen.Contains(rule.Name) || rule.Terms.Any(seen.Contains))
            {
                continue;
            }

            seen.Add(rule.Name);
            if (rule.WarningOnly)
            {
                warnings.Add(new FitWarning($"{rule.Name} gefordert – bitte prüfen", 0));
                continue;
            }

            var gap = GapFor(profile, rule.Terms.Append(rule.Name));
            requirements.Add(new FitRequirement(rule.Name, FitCoverage.Gap, AllSoft(hits, sentences, covered), gap?.De ?? "nicht im Profil"));
        }

        var penalties = settings.Penalties;
        var requiredGaps = requirements.Where(r => r is { Coverage: FitCoverage.Gap, Optional: false }).ToList();
        if (requiredGaps.Count > 0)
        {
            warnings.Add(new FitWarning(
                $"Gefordert, aber nicht im Profil: {string.Join(", ", requiredGaps.Select(r => r.Name))}",
                Math.Min(requiredGaps.Count * penalties.RequiredGap, penalties.RequiredGapMax)));
        }

        var german = RequiredGerman(text);
        if (german is { } required && MyGerman(profile) is var mine && required.Level > mine)
        {
            var steps = (int)required.Level - (int)mine;
            warnings.Add(new FitWarning(
                $"Deutsch {required.Level} gefordert („{required.Phrase}“), im Profil: {mine}",
                steps * penalties.GermanPerLevel));
        }

        var years = RequiredYears(title, sentences);
        var myYears = ProfileValidator.ExperienceMonths(profile, today) / 12.0;
        if (years is { } needed && needed.Years > myYears)
        {
            warnings.Add(new FitWarning(
                $"{needed.Phrase}: {needed.Years} Jahre erwartet, im Profil {myYears.ToString("0.#", CultureInfo.InvariantCulture)}",
                penalties.Experience));
        }

        if (OutsideRegions(settings, location, remoteLevel) is { } place)
        {
            warnings.Add(new FitWarning($"Außerhalb meiner Regionen ({place}) und nicht voll remote", penalties.OutsideRegion));
        }

        if (RemoteWishMissed(settings.RemotePreference, remoteLevel) is { } wish)
        {
            warnings.Add(new FitWarning(wish, penalties.RemotePreference));
        }

        if (settings.RankedLowerEmployerTypes.Contains(employerType))
        {
            var label = employerType == EmployerType.Temporary ? "Zeitarbeit" : "Personalvermittlung";
            warnings.Add(new FitWarning($"{label} – laut Suchprofil niedriger einordnen", penalties.EmployerType));
        }

        var ordered = requirements
            .OrderBy(r => r.Coverage)
            .ThenBy(r => r.Optional)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new FitResult(ordered, warnings, german?.Level, years?.Years, warnings.Sum(w => w.Penalty));
    }

    /// <summary>
    /// The place when a job is not fully remote and its location matches none of my places; <c>null</c> when it fits,
    /// when no places are set, or when the location is unknown. A remote posting („Remote (Deutschland)") counts as
    /// in my region when „Remote deutschlandweit" is on.
    /// </summary>
    internal static string? OutsideRegions(FitSettings settings, string? location, RemoteLevel remoteLevel)
    {
        if (settings.Places.Count == 0 || string.IsNullOrWhiteSpace(location) || remoteLevel == RemoteLevel.FullRemote)
        {
            return null;
        }

        var place = location.Trim();
        if (settings.RemoteGermany && place.StartsWith("Remote", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return settings.Places.Any(r => place.Contains(r, StringComparison.OrdinalIgnoreCase)) ? null : place;
    }

    /// <summary>The warning when the job does not meet the remote preference; unknown home office costs nothing.</summary>
    internal static string? RemoteWishMissed(RemotePreference preference, RemoteLevel level) => preference switch
    {
        RemotePreference.AtLeastHybrid when level == RemoteLevel.OnSite => "Nur vor Ort – Suchprofil: mindestens Hybrid",
        RemotePreference.FullRemoteOnly when level is RemoteLevel.OnSite or RemoteLevel.Hybrid => "Nicht voll remote – Suchprofil: nur voll remote",
        _ => null,
    };

    [GeneratedRegex(@"\b(?:oder|or|bzw\.?|alternativ)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex AlternativeWord();

    private static KnownGap? GapFor(CandidateProfile profile, IEnumerable<string> names)
    {
        var set = new HashSet<string>(names.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        return profile.KnownGaps.FirstOrDefault(g => set.Contains(g.Skill.Trim()) || g.Aliases.Any(a => set.Contains(a.Trim())));
    }

    /// <summary>
    /// Optional only if every mention is soft: its sentence has an optional marker („von Vorteil"), or — for a gap —
    /// offers it as an alternative to a skill I cover („Angular oder Vue.js").
    /// </summary>
    private bool AllSoft(
        IEnumerable<(string Term, int Index)> hits,
        IReadOnlyList<(int Start, string Text)> sentences,
        TermMatcher? coveredAlternatives) =>
        hits.All(h =>
        {
            var sentence = SentenceAt(sentences, h.Index);
            return _optionalMarkers.FindFirst(sentence) is not null
                || (coveredAlternatives is not null && AlternativeWord().IsMatch(sentence) && coveredAlternatives.FindFirst(sentence) is not null);
        });

    private static string SentenceAt(IReadOnlyList<(int Start, string Text)> sentences, int index)
    {
        for (var i = sentences.Count - 1; i >= 0; i--)
        {
            if (sentences[i].Start <= index)
            {
                return sentences[i].Text;
            }
        }

        return string.Empty;
    }

    /// <summary>Splits at line breaks, bullets and sentence ends („. " — not inside „ASP.NET").</summary>
    public static IReadOnlyList<(int Start, string Text)> Sentences(string text)
    {
        var result = new List<(int, string)>();
        var start = 0;
        foreach (Match boundary in SentenceBoundary().Matches(text))
        {
            result.Add((start, text[start..boundary.Index]));
            start = boundary.Index + boundary.Length;
        }

        result.Add((start, text[start..]));
        return result;
    }

    private static LanguageLevel MyGerman(CandidateProfile profile) =>
        profile.Languages.FirstOrDefault(l => l.Name.Trim() is "Deutsch" or "German")?.Level ?? LanguageLevel.A1;

    public static (LanguageLevel Level, string Phrase)? RequiredGerman(string text)
    {
        (LanguageLevel Level, string Phrase)? best = null;
        foreach (Match match in GermanCefr().Matches(text))
        {
            var token = match.Groups["a"].Success ? match.Groups["a"].Value : match.Groups["b"].Value;
            Consider(Enum.Parse<LanguageLevel>(token.ToUpperInvariant()), match.Value);
        }

        foreach (Match match in GermanC1Words().Matches(text))
        {
            Consider(LanguageLevel.C1, match.Value);
        }

        foreach (Match match in GermanB2Words().Matches(text))
        {
            Consider(LanguageLevel.B2, match.Value);
        }

        return best;

        void Consider(LanguageLevel level, string phrase)
        {
            if (best is null || level > best.Value.Level)
            {
                best = (level, phrase.Trim());
            }
        }
    }

    public static (int Years, string Phrase)? RequiredYears(string title, IReadOnlyList<(int Start, string Text)> sentences)
    {
        (int Years, string Phrase)? best = null;
        foreach (var (_, sentence) in sentences)
        {
            if (!ExperienceWord().IsMatch(sentence))
            {
                continue;
            }

            foreach (Match match in YearsNumber().Matches(sentence))
            {
                Consider(int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture), match.Value);
            }

            if (Langjaehrig().Match(sentence) is { Success: true } many)
            {
                Consider(5, many.Value);
            }
            else if (Mehrjaehrig().Match(sentence) is { Success: true } several)
            {
                Consider(3, several.Value);
            }
        }

        if (SeniorTitle().Match(title) is { Success: true } senior)
        {
            Consider(5, $"„{senior.Value}\" im Titel");
        }

        return best;

        void Consider(int years, string phrase)
        {
            if (years is > 0 and <= 30 && (best is null || years > best.Value.Years))
            {
                best = (years, phrase.Trim());
            }
        }
    }

    [GeneratedRegex(@"\n+|[•·▪]\s*|(?<=[a-zäöüß)\]])[.!?;](?=\s)", RegexOptions.None, 250)]
    private static partial Regex SentenceBoundary();

    [GeneratedRegex(@"(?:deutsch(?:kenntnisse|kenntnissen|sprachkenntnisse|sprachig)?\b|german)[^.\n]{0,50}?\b(?<a>[ABC][12])\b|\b(?<b>[ABC][12])\b[^.\n]{0,30}?(?:deutsch(?:kenntnisse|kenntnissen|sprachkenntnisse|sprachig)?\b|german)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex GermanCefr();

    [GeneratedRegex(
        @"(?:verhandlungssicher|fließend|fliessend|sehr gut|exzellent|hervorragend|ausgezeichnet|perfekt)\w*\s+(?:\w+\s+){0,2}?deutsch(?:kenntnisse|kenntnissen|sprachkenntnisse|sprachig)?\b" +
        @"|deutsch(?:kenntnisse|kenntnissen|sprachkenntnisse|sprachig)?\b\s+(?:\w+\s+){0,3}?(?:verhandlungssicher|fließend|fliessend|auf muttersprach\w*|muttersprachlich\w*)" +
        @"|(?:fluent|business fluent|excellent|native)\s+(?:\w+\s+)?german|german\s*\(?(?:fluent|native)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex GermanC1Words();

    [GeneratedRegex(@"\bgute\w*\s+(?:\w+\s+){0,2}?deutsch(?:kenntnisse|kenntnissen|sprachkenntnisse|sprachig)?\b|\bgood\s+german", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex GermanB2Words();

    [GeneratedRegex(@"erfahrung|experience|berufserfahrung|praxis", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex ExperienceWord();

    [GeneratedRegex(@"(?<!\d)(?<n>\d{1,2})\s*\+?\s*(?:-\s*\d{1,2}\s*)?(?:jahre?n?|years?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex YearsNumber();

    [GeneratedRegex(@"\blangjährig\w*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex Langjaehrig();

    [GeneratedRegex(@"\bmehrjährig\w*|\bseveral years\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex Mehrjaehrig();

    [GeneratedRegex(@"\b(?:Senior|Sr\.|Lead|Principal)\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex SeniorTitle();
}
