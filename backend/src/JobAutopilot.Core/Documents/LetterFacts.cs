using System.Globalization;
using System.Text.RegularExpressions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.SearchProfiles;

namespace JobAutopilot.Core.Documents;

public enum LetterFactKind
{
    Personal,
    Summary,
    Experience,
    Achievement,
    Skill,
    Gap,
    Language,
    Project,
    Education,
    Certificate,
    Preference,
}

/// <summary>One fact the letter may use, with a stable id (<c>exp0.b2</c>, <c>skill:csharp</c>, <c>gap:kubernetes</c>).</summary>
public sealed record LetterFact(string Id, LetterFactKind Kind, string Text);

/// <summary>The vacancy as the letter sees it. <see cref="Description"/> is untrusted text from an external source.</summary>
public sealed record LetterVacancy(string Title, string Company, string? Location, string? Description, string? SourceLabel);

/// <summary>Everything a letter may be built from — and nothing else (AGENTS.md: no invented facts).</summary>
public sealed record LetterInput(
    LetterVacancy Vacancy,
    ToneResult Tone,
    IReadOnlyList<LetterFact> Facts,
    FitResult Fit,
    string CandidateName,
    string? Website,
    DateOnly Today)
{
    public IEnumerable<LetterFact> OfKind(LetterFactKind kind) => Facts.Where(f => f.Kind == kind);
}

/// <summary>
/// Picks the facts for one letter from the profile: personal links, summaries, the experience entries, the
/// achievements that fit the posting best (by tags), the skills the posting asks for with their level, the known gaps
/// with the honest phrasing, languages, projects, education — and salary / earliest start from the Suchprofil only
/// when the posting asks for them. Texts are taken in the document language where the profile has them (<c>*De</c>).
/// </summary>
public static partial class LetterFactsBuilder
{
    public const int DefaultMaxAchievements = 8;

    public static LetterInput Build(
        CandidateProfile profile,
        LetterVacancy vacancy,
        FitResult fit,
        SearchProfile? searchProfile,
        DateOnly today,
        int maxAchievements = DefaultMaxAchievements)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(vacancy);
        ArgumentNullException.ThrowIfNull(fit);

        var tone = ToneDetector.Detect(vacancy.Title, vacancy.Description);
        var de = tone.Language == DocumentLanguage.De;
        var terms = new VacancyTerms(profile, vacancy.Title, vacancy.Description);
        var facts = new List<LetterFact>();

        var personal = profile.Personal;
        facts.Add(new("personal.name", LetterFactKind.Personal, $"Name: {personal.Name}"));
        AddIf(facts, "personal.city", LetterFactKind.Personal, personal.City, v => $"{(de ? "Wohnort" : "City")}: {v}");
        AddIf(facts, "personal.website", LetterFactKind.Personal, personal.Website, v => $"Website: {Links.Display(v)}");
        AddIf(facts, "personal.github", LetterFactKind.Personal, personal.Github, v => $"GitHub: {Links.Display(v)}");
        AddIf(facts, "personal.linkedin", LetterFactKind.Personal, personal.Linkedin, v => $"LinkedIn: {Links.Display(v)}");

        var years = ProfileValidator.ExperienceMonths(profile, today) / 12;
        facts.Add(new("experience.years", LetterFactKind.Experience, de
            ? $"Berufserfahrung insgesamt: {years} Jahre"
            : $"Professional experience in total: {years} years"));

        foreach (var key in profile.Summary.Keys.Where(k => !k.EndsWith(".de", StringComparison.OrdinalIgnoreCase)))
        {
            facts.Add(new($"summary.{key}", LetterFactKind.Summary, Localized.Summary(profile, key, tone.Language) ?? string.Empty));
        }

        var achievements = new List<(LetterFact Fact, int Score, int Order)>();
        foreach (var (entry, i) in profile.Experience.Select((e, i) => (e, i)))
        {
            var title = de ? entry.TitleDe ?? entry.Title : entry.Title;
            var where = string.Join(", ", new[] { de ? entry.LocationDe ?? entry.Location : entry.Location, entry.Remote ? "remote" : null }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            facts.Add(new($"exp{i}", LetterFactKind.Experience,
                $"{title}, {entry.Company} ({Dates.Period(entry.From, entry.To, tone.Language)}{(where.Length > 0 ? ", " + where : "")})"));
            foreach (var (bullet, j) in entry.Bullets.Select((b, j) => (b, j)))
            {
                var text = de ? bullet.TextDe ?? bullet.Text : bullet.Text;
                achievements.Add((new($"exp{i}.b{j}", LetterFactKind.Achievement, text), terms.Score(bullet.Tags, bullet.Text), achievements.Count));
            }
        }

        facts.AddRange(achievements
            .OrderByDescending(a => a.Score).ThenBy(a => a.Order)
            .Take(maxAchievements)
            .OrderBy(a => a.Order)
            .Select(a => a.Fact));

        var skills = profile.Skills
            .Where(s => s.Level is not SkillLevel.None)
            .OrderByDescending(terms.Mentions)
            .ThenBy(s => s.Level)
            .ThenByDescending(s => s.Years ?? 0)
            .Take(20);
        foreach (var skill in skills)
        {
            facts.Add(new($"skill:{VacancyTerms.Normalize(skill.Name)}", LetterFactKind.Skill,
                $"{skill.Name}: {LevelText(skill.Level, de)}{(skill.Years is { } y ? $", {y.ToString("0.#", CultureInfo.InvariantCulture)} {(de ? "Jahre" : "years")}" : "")}"));
        }

        foreach (var gap in fit.Requirements.Where(r => r.Coverage == FitCoverage.Gap))
        {
            var known = profile.KnownGaps.FirstOrDefault(g => SameSkill(g, gap.Name));
            var phrasing = known is null ? null : de ? known.De : known.En;
            facts.Add(new($"gap:{VacancyTerms.Normalize(gap.Name)}", LetterFactKind.Gap,
                $"{gap.Name}{(gap.Optional ? (de ? " (optional)" : " (nice to have)") : "")}: "
                + (phrasing ?? (de ? "keine Erfahrung (nicht im Profil)" : "no experience (not in the profile)"))));
        }

        foreach (var language in profile.Languages)
        {
            var name = de ? language.NameDe ?? language.Name : language.Name;
            var note = de ? language.NoteDe ?? language.Note : language.Note;
            facts.Add(new($"lang:{VacancyTerms.Normalize(language.Name)}", LetterFactKind.Language,
                $"{name}: {LanguageLevelText(language.Level, de)}{(note is null ? "" : $" ({note})")}"));
        }

        foreach (var (project, i) in profile.Projects.Select((p, i) => (p, i)))
        {
            var description = de ? project.DescriptionDe ?? project.Description : project.Description;
            facts.Add(new($"proj{i}", LetterFactKind.Project,
                $"{project.Name}: {description}{(project.Url is null ? "" : $" ({Links.Display(project.Url)})")}"));
        }

        foreach (var (education, i) in profile.Education.Select((e, i) => (e, i)))
        {
            facts.Add(new($"edu{i}", LetterFactKind.Education,
                $"{(de ? education.DegreeDe ?? education.Degree : education.Degree)}, "
                + $"{(de ? education.InstitutionDe ?? education.Institution : education.Institution)} "
                + $"({Dates.Period(education.From, education.To, tone.Language)})"));
        }

        foreach (var (certificate, i) in profile.Certificates.Select((c, i) => (c, i)))
        {
            facts.Add(new($"cert{i}", LetterFactKind.Certificate,
                string.Join(", ", new[] { certificate.Name, certificate.Issuer, certificate.Date }.Where(s => !string.IsNullOrWhiteSpace(s)))));
        }

        var postingText = $"{vacancy.Title}\n{vacancy.Description}";
        if (searchProfile is not null)
        {
            if (AsksForSalary().IsMatch(postingText) && Formats.SalaryRange(searchProfile.SalaryMin, searchProfile.SalaryMax, tone.Language) is { } salary)
            {
                facts.Add(new("pref.salary", LetterFactKind.Preference, $"{(de ? "Gehaltsvorstellung" : "Salary expectation")}: {salary}"));
            }

            // Always when set (08c): otherwise the letter says „ab sofort" while I can only start later.
            if (searchProfile.EarliestStart is { Length: > 0 } start)
            {
                facts.Add(new("pref.start", LetterFactKind.Preference,
                    $"{(de ? "Frühester Eintritt" : "Earliest start")}: {Formats.Date(start, tone.Language)}"));
            }
        }

        return new LetterInput(vacancy, tone, facts, fit, personal.Name, personal.Website, today);
    }

    public static bool SameSkill(KnownGap gap, string name)
    {
        ArgumentNullException.ThrowIfNull(gap);
        var key = VacancyTerms.Normalize(name);
        return gap.Aliases.Prepend(gap.Skill).Any(a => VacancyTerms.Normalize(a) == key);
    }

    private static void AddIf(List<LetterFact> facts, string id, LetterFactKind kind, string? value, Func<string, string> text)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            facts.Add(new(id, kind, text(value)));
        }
    }

    private static string LevelText(SkillLevel level, bool de) => level switch
    {
        SkillLevel.Production => de ? "beruflich eingesetzt" : "used at work",
        SkillLevel.Project => de ? "in eigenen Projekten" : "own projects",
        SkillLevel.Learning => de ? "lerne ich gerade (keine Berufserfahrung)" : "learning (no professional experience)",
        _ => de ? "keine Erfahrung" : "no experience",
    };

    private static string LanguageLevelText(LanguageLevel level, bool de) =>
        level == LanguageLevel.Native ? (de ? "Muttersprache" : "native") : level.ToString();

    [GeneratedRegex(@"Gehaltsvorstellung|Gehaltswunsch|Gehaltsangabe|Gehaltsvorstellungen|salary expectation|salary requirements|expected salary", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex AsksForSalary();

    [GeneratedRegex(@"Eintrittstermin|frühestmöglich|frühesten Eintritt|Starttermin|Verfügbarkeit|Kündigungsfrist|earliest (possible )?start|start date|availability|notice period", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex AsksForStart();
}
