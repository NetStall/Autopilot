using System.Globalization;
using System.Text.RegularExpressions;
using JobAutopilot.Core.Extraction;
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

/// <summary>
/// One requirement found in the posting. <see cref="Note"/>: level, or the honest phrasing for a gap.
/// <see cref="Optional"/> = nice to have or an alternative; <see cref="Importance"/> decides a gap's penalty.
/// </summary>
public sealed record FitRequirement(
    string Name,
    FitCoverage Coverage,
    bool Optional,
    string Note,
    RequirementImportance Importance = RequirementImportance.Required,
    bool Method = false);

/// <summary>Years of experience a posting asks for; <see cref="Strong"/> = more than „Senior" in the title alone.</summary>
public sealed record ExperienceRequirement(int Years, string Phrase, bool Strong);

public sealed record FitWarning(string Text, int Penalty);

/// <summary>
/// What the fit check looks at; location, remote level and the home-office share (the source's field, else the text)
/// decide the region warning.
/// </summary>
public sealed record FitInput(
    string Title,
    string? Description,
    EmployerType EmployerType,
    string? Location,
    RemoteLevel RemoteLevel,
    int? HomeOfficePercent = null,
    RequirementExtraction? Extraction = null);

/// <summary>Where the requirements of a fit check come from: the rules or a language model's extraction.</summary>
public enum FitSource
{
    Rules,
    Ai,
}

/// <summary>Result of <see cref="FitAnalyzer.Analyze"/>; <see cref="Penalty"/> is subtracted for the ranking.</summary>
public sealed record FitResult(
    IReadOnlyList<FitRequirement> Requirements,
    IReadOnlyList<FitWarning> Warnings,
    LanguageLevel? RequiredGerman,
    int? RequiredYears,
    int Penalty,
    bool Unreachable = false,
    FitSource Source = FitSource.Rules)
{
    public static readonly FitResult Empty = new([], [], null, null, 0);
}

/// <summary>
/// Rule-based fit check of one vacancy against the profile — no AI. Requirements are the profile's skills (by name
/// and aliases), the configured common requirements and the technologies of <see cref="TechnologyCatalog"/> that
/// the profile does not list, found in title and description; each is covered, partial or
/// a gap (with the honest phrasing from <c>knownGaps</c>). Skills count only by name or a specific alias, never by a
/// generic one (<see cref="SkillTerms"/>). How important a requirement is comes from the posting's sections („Was du
/// mitbringst" = must-have, „Sprachen &amp; Technologien" = likely required, „Ein Plus, aber kein Muss" = nice to have)
/// and from markers in its line („von Vorteil", „(Pflicht)", „mindestens"); a missing must-have costs more than a
/// missing nice-to-have. A job outside my places that is not fully remote is practically not reachable when it is on
/// site only or states at most 60 % home office; with an unknown share it only costs a moderate penalty. German level and years of
/// experience are compared with the profile and give warnings, never an exclusion. With an extraction (step 07a) the
/// requirements, German level and years come from the model instead (<see cref="ExtractionNormalizer"/>); a required
/// domain I don't have is a must-have gap. Pure and deterministic.
/// </summary>
public sealed partial class FitAnalyzer(FitOptions options)
{
    private readonly (FitRequirementRule Rule, TermMatcher Matcher)[] _common =
        [.. options.CommonRequirements.Select(r => (r, new TermMatcher(r.Terms.Append(r.Name))))];

    private readonly TermMatcher _optionalMarkers = new(options.OptionalMarkers);

    private readonly TermMatcher _mustHaveMarkers = new(options.EffectiveMustHaveMarkers);

    private readonly Func<string, RequirementSections> _sections = RequirementSections.Parser(options);

    private readonly IReadOnlyList<(Technology Technology, TermMatcher Matcher)> _technologies =
        options.UseTechnologyCatalog ? TechnologyCatalog.Matchers : [];

    public FitResult Analyze(CandidateProfile profile, string title, string? description, EmployerType employerType, DateOnly today) =>
        Analyze(profile, new FitInput(title, description, employerType, null, RemoteLevel.Unknown), today, FitSettings.Neutral);

    /// <param name="settings">From the Suchprofil: penalties, my region, remote wish, employer types ranked lower.</param>
    public FitResult Analyze(CandidateProfile profile, FitInput input, DateOnly today, FitSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        var (title, description, employerType, location, remoteLevel, homeOfficePercent, extraction) = input;
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(title);
        var text = string.IsNullOrWhiteSpace(description) ? title : title + "\n" + description;
        var sentences = Sentences(text);
        var sections = _sections(text);
        var requirements = new List<FitRequirement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var covered = new TermMatcher(profile.Skills.Where(s => s.Level == SkillLevel.Production).SelectMany(SkillTerms.Evidence));

        foreach (var skill in profile.Skills.Where(s => extraction is null && !string.IsNullOrWhiteSpace(s.Name)))
        {
            var matcher = new TermMatcher(SkillTerms.Evidence(skill));
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
            var importance = ImportanceOf(hits, sentences, sections, coverage == FitCoverage.Gap ? covered : null);
            requirements.Add(new FitRequirement(skill.Name, coverage, importance <= RequirementImportance.NiceToHave, note, importance));
        }

        var warnings = new List<FitWarning>();
        foreach (var (rule, matcher) in _common)
        {
            var hits = matcher.FindAll(text).ToList();
            if (hits.Count == 0 || seen.Contains(rule.Name) || rule.Terms.Any(seen.Contains))
            {
                continue;
            }

            seen.UnionWith(rule.Terms.Append(rule.Name));
            if (rule.WarningOnly)
            {
                warnings.Add(new FitWarning($"{rule.Name} gefordert – bitte prüfen", 0));
                continue;
            }

            if (extraction is not null)
            {
                continue;
            }

            var gap = GapFor(profile, rule.Terms.Append(rule.Name));
            var importance = ImportanceOf(hits, sentences, sections, covered);
            requirements.Add(new FitRequirement(
                rule.Name, FitCoverage.Gap, importance <= RequirementImportance.NiceToHave, gap?.De ?? "nicht im Profil", importance));
        }

        if (extraction is null)
        {
            AddTechnologyGaps(profile, text, sentences, sections, covered, seen, requirements);
        }
        else
        {
            AddExtracted(profile, extraction, sentences, requirements);
        }

        var penalties = settings.Penalties;
        AddGapWarnings(requirements, penalties, options.MethodsGapMax, warnings);

        var german = GermanRequirementDetector.Detect(title, description);
        var (germanLevel, germanPhrase) = extraction switch
        {
            { GermanNotRequired: true } => (null, null),
            { GermanLevel: { } extracted } => ((LanguageLevel?)extracted, "laut KI"),
            _ => (german.Level, german.Phrase),
        };
        if (germanLevel is { } level && MyGerman(profile) is var mine && level > mine)
        {
            var steps = (int)level - (int)mine;
            warnings.Add(new FitWarning(
                $"Deutsch {level} gefordert („{germanPhrase}“), im Profil: {mine}",
                steps * penalties.GermanPerLevel));
        }

        // With an extraction only a stated number of years counts — never years derived from „Senior" (07a2).
        var years = extraction is not null ? ExtractedYears(extraction) : RequiredYears(title, sentences);
        var myYears = ProfileValidator.ExperienceMonths(profile, today) / 12.0;
        if (years is { } needed && needed.Years > myYears)
        {
            warnings.Add(new FitWarning(
                $"{needed.Phrase}: {needed.Years} Jahre erwartet, im Profil {myYears.ToString("0.#", CultureInfo.InvariantCulture)}"
                + (needed.Strong ? " – deutliches Signal" : string.Empty),
                needed.Strong ? penalties.ExperienceStrong : penalties.Experience));
        }

        var unreachable = false;
        if (OutsideRegions(settings, location, remoteLevel) is { } place)
        {
            var share = homeOfficePercent ?? extraction?.HomeOfficePercent ?? HomeOfficeShare.Find(text);
            unreachable = remoteLevel == RemoteLevel.OnSite || share <= options.ReachableFromHomeOfficePercent;
            warnings.Add(
                unreachable ? new FitWarning(
                    $"Praktisch nicht erreichbar: {place}, {(share is { } percent ? $"Homeoffice bis {percent.ToString(CultureInfo.InvariantCulture)} %" : "nur vor Ort")}",
                    penalties.Unreachable)
                : share is null ? new FitWarning($"Außerhalb meiner Regionen ({place}), Homeoffice-Anteil unbekannt", penalties.OutsideRegionUnknownShare)
                : new FitWarning($"Außerhalb meiner Regionen ({place}) und nicht voll remote", penalties.OutsideRegion));
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
        return new FitResult(ordered, warnings, germanLevel, years?.Years, warnings.Sum(w => w.Penalty), unreachable,
            extraction is null ? FitSource.Rules : FitSource.Ai);
    }

    /// <summary>
    /// The extracted requirements, resolved to my skills and the catalog, with the coverage of my level. A missing method
    /// (DDD, CQRS …) is nice to have unless its line marks it as required („zwingend", „(Pflicht)").
    /// </summary>
    private void AddExtracted(
        CandidateProfile profile, RequirementExtraction extraction, IReadOnlyList<(int Start, string Text)> sentences, List<FitRequirement> requirements)
    {
        foreach (var found in ExtractionNormalizer.Resolve(extraction, profile))
        {
            var resolved = found;
            if (resolved.Method && resolved.Importance > RequirementImportance.NiceToHave
                && !sentences.Any(s => new TermMatcher([resolved.Name]).FindFirst(s.Text) is not null && _mustHaveMarkers.FindFirst(s.Text) is not null))
            {
                resolved = resolved with { Importance = RequirementImportance.NiceToHave };
            }

            var names = resolved.Skill?.AllNames() ?? [resolved.Name];
            var gap = GapFor(profile, names);
            var (coverage, note) = resolved.Skill?.Level switch
            {
                SkillLevel.Production => (FitCoverage.Covered, "beruflich" + (resolved.Skill.Years is { } y ? $", {y.ToString(CultureInfo.InvariantCulture)} J." : string.Empty)),
                SkillLevel.Project => (FitCoverage.Partial, gap?.De ?? "eigene Projekte"),
                SkillLevel.Learning => (FitCoverage.Partial, gap?.De ?? "lerne ich gerade"),
                SkillLevel.None => (FitCoverage.Gap, gap?.De ?? "keine Erfahrung"),
                _ => (FitCoverage.Gap, gap?.De ?? (resolved.Domain ? "Fachgebiet nicht im Profil" : "nicht im Profil")),
            };
            requirements.Add(new FitRequirement(
                resolved.Name, coverage, resolved.Importance <= RequirementImportance.NiceToHave, note, resolved.Importance, resolved.Method && resolved.Skill is null));
        }
    }

    /// <summary>Years from the extraction: only the stated minimum (strong from 5 years).</summary>
    private static ExperienceRequirement? ExtractedYears(RequirementExtraction extraction) =>
        extraction.MinYears is { } years
            ? new ExperienceRequirement(years, $"laut KI mindestens {years.ToString(CultureInfo.InvariantCulture)} Jahre", years >= 5)
            : null;

    /// <summary>
    /// Technologies of the dictionary the profile does not list at all: a gap wherever they are asked for — not when
    /// they only appear in the tasks, the offer or the company description.
    /// </summary>
    private void AddTechnologyGaps(
        CandidateProfile profile,
        string text,
        IReadOnlyList<(int Start, string Text)> sentences,
        RequirementSections sections,
        TermMatcher covered,
        HashSet<string> seen,
        List<FitRequirement> requirements)
    {
        if (_technologies.Count == 0)
        {
            return;
        }

        var inProfile = new HashSet<string>(profile.Skills.SelectMany(s => s.AllNames()).Select(SkillTerms.Key), StringComparer.Ordinal);
        foreach (var (technology, matcher) in _technologies)
        {
            if (technology.AllNames().Any(n => seen.Contains(n) || inProfile.Contains(SkillTerms.Key(n)))
                || (technology.Parent is { } parent && (seen.Contains(parent) || requirements.Any(r => r.Name == parent))))
            {
                continue;
            }

            var hits = matcher.FindAll(text).Where(h => sections.At(h.Index) != SectionKind.Other).ToList();
            if (hits.Count == 0)
            {
                continue;
            }

            seen.UnionWith(technology.AllNames());
            var importance = ImportanceOf(hits, sentences, sections, covered);
            requirements.Add(new FitRequirement(
                technology.Name, FitCoverage.Gap, importance <= RequirementImportance.NiceToHave, "nicht im Profil", importance));
        }
    }

    /// <summary>
    /// Gap penalties by importance: must-have gaps up to <c>MustHaveGapMax</c>, all others sharing
    /// <c>RequiredGapMax</c> (most important first).
    /// </summary>
    private static void AddGapWarnings(List<FitRequirement> requirements, SearchPenalties penalties, int methodsMax, List<FitWarning> warnings)
    {
        // Methods (DDD, CQRS, Event Sourcing …) at nice-to-have level share one small cap, unless marked as required.
        var methods = requirements.Where(r => r.Method && r.Coverage == FitCoverage.Gap && r.Importance == RequirementImportance.NiceToHave).ToList();
        if (methods.Count > 0)
        {
            warnings.Add(new FitWarning(
                $"Methoden, nicht im Profil: {string.Join(", ", methods.Select(r => r.Name))}",
                Math.Min(methods.Count * penalties.NiceToHaveGap, Math.Max(methodsMax, 0))));
        }

        var budget = penalties.RequiredGapMax;
        foreach (var (importance, perGap, label) in new[]
                 {
                     (RequirementImportance.MustHave, penalties.MustHaveGap, "Muss-Anforderung, nicht im Profil"),
                     (RequirementImportance.LikelyRequired, penalties.LikelyRequiredGap, "Im Tech-Stack, nicht im Profil"),
                     (RequirementImportance.Required, penalties.RequiredGap, "Gefordert, aber nicht im Profil"),
                     (RequirementImportance.NiceToHave, penalties.NiceToHaveGap, "Ein Plus, nicht im Profil"),
                 })
        {
            var gaps = requirements.Where(r => r.Coverage == FitCoverage.Gap && r.Importance == importance && !methods.Contains(r)).ToList();
            if (gaps.Count == 0)
            {
                continue;
            }

            int points;
            if (importance == RequirementImportance.MustHave)
            {
                points = Math.Min(gaps.Count * perGap, Math.Max(penalties.MustHaveGapMax, 0));
            }
            else
            {
                points = Math.Min(gaps.Count * perGap, Math.Max(budget, 0));
                budget -= points;
            }

            warnings.Add(new FitWarning($"{label}: {string.Join(", ", gaps.Select(r => r.Name))}", points));
        }
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

    /// <summary>„oder", „bzw." — and lists of examples („z. B. MSSQL, PostgreSQL, MySQL"): one of them is enough.</summary>
    [GeneratedRegex(@"\b(?:oder|or|bzw\.?|alternativ\w*|beispielsweise|etwa)\b|\bz\.\s?B\.|\be\.\s?g\.|\bsuch\s+as\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex AlternativeWord();

    private static KnownGap? GapFor(CandidateProfile profile, IEnumerable<string> names)
    {
        var set = new HashSet<string>(names.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        return profile.KnownGaps.FirstOrDefault(g => set.Contains(g.Skill.Trim()) || g.Aliases.Any(a => set.Contains(a.Trim())));
    }

    /// <summary>
    /// The most important mention wins. Per mention: an alternative to a skill I cover („Angular oder Vue.js", gaps
    /// only) costs nothing; a sentence with „von Vorteil" &amp; co. is nice to have, one with „(Pflicht)", „zwingend",
    /// „mindestens" a must-have — whatever the section; otherwise the section decides (must-have, stack = likely
    /// required, nice to have), and without one it is required.
    /// </summary>
    private RequirementImportance ImportanceOf(
        IEnumerable<(string Term, int Index)> hits,
        IReadOnlyList<(int Start, string Text)> sentences,
        RequirementSections sections,
        TermMatcher? coveredAlternatives)
    {
        var best = RequirementImportance.Alternative;
        var offeredAsAlternative = false;
        foreach (var (_, index) in hits)
        {
            var sentence = SentenceAt(sentences, index);
            if (coveredAlternatives is not null && AlternativeWord().IsMatch(sentence) && coveredAlternatives.FindFirst(sentence) is not null)
            {
                offeredAsAlternative = true;
                continue;
            }

            var importance =
                _optionalMarkers.FindFirst(sentence) is not null ? RequirementImportance.NiceToHave
                : _mustHaveMarkers.FindFirst(sentence) is not null ? RequirementImportance.MustHave
                : sections.At(index) switch
                {
                    SectionKind.MustHave => RequirementImportance.MustHave,
                    SectionKind.Stack => RequirementImportance.LikelyRequired,
                    SectionKind.NiceToHave => RequirementImportance.NiceToHave,
                    _ => RequirementImportance.Required,
                };
            if (importance > best)
            {
                best = importance;
            }
        }

        // Accepted instead of a skill I have („Vue.js oder React") and nowhere required: not a gap that costs points.
        return offeredAsAlternative && best <= RequirementImportance.NiceToHave ? RequirementImportance.Alternative : best;
    }

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
        profile.Languages.FirstOrDefault(l => l.Name.Trim() is "Deutsch" or "German" || l.NameDe?.Trim() == "Deutsch")?.Level ?? LanguageLevel.A1;

    /// <summary>The required German level and the phrase (see <see cref="GermanRequirementDetector"/>).</summary>
    public static (LanguageLevel Level, string Phrase)? RequiredGerman(string text)
    {
        var found = GermanRequirementDetector.Detect(text, null);
        return found.Level is { } level ? (level, found.Phrase ?? string.Empty) : null;
    }

    /// <summary>
    /// Years of experience asked for. Strong signals: „langjährige Erfahrung", a number of five years or more („5+
    /// Jahre"), Senior/Lead in an experience sentence („mehrjährige Erfahrung in der Senior-Softwareentwicklung") or
    /// Lead/Principal/Staff in the title. „Senior" in the title alone is a weak signal (5 years).
    /// </summary>
    public static ExperienceRequirement? RequiredYears(string title, IReadOnlyList<(int Start, string Text)> sentences)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(sentences);
        var signals = new List<ExperienceRequirement>();
        foreach (var (_, sentence) in sentences)
        {
            if (!ExperienceWord().IsMatch(sentence))
            {
                continue;
            }

            foreach (Match match in YearsNumber().Matches(sentence))
            {
                var asked = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
                Consider(asked, match.Value, asked >= 5);
            }

            if (Langjaehrig().Match(sentence) is { Success: true } many)
            {
                Consider(5, many.Value, strong: true);
            }

            if (SeniorWord().Match(sentence) is { Success: true } seniority)
            {
                Consider(5, $"{(Mehrjaehrig().Match(sentence) is { Success: true } m ? m.Value + " … " : string.Empty)}{seniority.Value}-Erfahrung gefordert", strong: true);
            }
            else if (Mehrjaehrig().Match(sentence) is { Success: true } several)
            {
                Consider(3, several.Value, strong: false);
            }
        }

        if (LeadTitle().Match(title) is { Success: true } lead)
        {
            Consider(5, $"„{lead.Value}\" im Titel", strong: true);
        }
        else if (SeniorTitle().Match(title) is { Success: true } senior)
        {
            Consider(5, $"„{senior.Value}\" im Titel", strong: false);
        }

        if (signals.Count == 0)
        {
            return null;
        }

        // The most years asked for; with any strong signal the requirement is strong and names that signal.
        var years = signals.Max(x => x.Years);
        var strongest = signals.Where(x => x.Strong).OrderByDescending(x => x.Years).FirstOrDefault();
        return strongest is not null
            ? new ExperienceRequirement(years, strongest.Phrase, Strong: true)
            : signals.OrderByDescending(x => x.Years).First();

        void Consider(int years, string phrase, bool strong)
        {
            if (years is > 0 and <= 30)
            {
                signals.Add(new ExperienceRequirement(years, phrase.Trim(), strong));
            }
        }
    }

    [GeneratedRegex(@"\n+|[•·▪]\s*|(?<=[a-zäöüß)\]])[.!?;](?=\s)", RegexOptions.None, 250)]
    private static partial Regex SentenceBoundary();




    [GeneratedRegex(@"erfahrung|experience|berufserfahrung|praxis", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex ExperienceWord();

    [GeneratedRegex(@"(?<!\d)(?<n>\d{1,2})\s*\+?\s*(?:-\s*\d{1,2}\s*)?(?:jahre?n?|years?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex YearsNumber();

    [GeneratedRegex(@"\blangjährig\w*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex Langjaehrig();

    [GeneratedRegex(@"\bmehrjährig\w*|\bseveral years\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex Mehrjaehrig();

    [GeneratedRegex(@"\b(?:Senior|Sr\.)", RegexOptions.CultureInvariant, 250)]
    private static partial Regex SeniorTitle();

    [GeneratedRegex(@"\b(?:Lead|Principal|Staff)\b|\bTeamleit\w*|\bTech[\s-]?Lead\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex LeadTitle();

    /// <summary>Seniority inside an experience sentence: „Erfahrung in der Senior Softwareentwicklung", „als Lead".</summary>
    [GeneratedRegex(@"\b(?:Senior|Lead|Principal|Staff)\b|\bTeamleit\w*|\bFührungserfahrung\b", RegexOptions.CultureInvariant, 250)]
    private static partial Regex SeniorWord();
}
