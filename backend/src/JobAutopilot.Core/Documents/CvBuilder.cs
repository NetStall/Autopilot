using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Core.Documents;

/// <summary>
/// Manual choices in the review: another focus, bullets hidden or shown, their order, skill groups hidden and ordered.
/// Ids are those of <see cref="CvBullet.Id"/> (<c>exp0.b3</c>) and the group names.
/// </summary>
public sealed record CvOptions(
    string? Focus,
    IReadOnlyList<string> HiddenBullets,
    IReadOnlyList<string> ShownBullets,
    IReadOnlyList<string> BulletOrder,
    IReadOnlyList<string> HiddenSkillGroups,
    IReadOnlyList<string> SkillGroupOrder)
{
    public static readonly CvOptions Default = new(null, [], [], [], [], []);

    public const int MaxIds = 200;
}

public sealed record CvContact(string Text, string? Url);

/// <summary><see cref="AutoHidden"/>: hidden by the builder (beyond the per-entry limit or to keep one page), not by me.</summary>
public sealed record CvBullet(string Id, string Text, int Score, bool Hidden, bool AutoHidden);

public sealed record CvExperience(string Id, string Title, string Company, string? Where, string Period, IReadOnlyList<CvBullet> Bullets)
{
    public IEnumerable<CvBullet> Visible => Bullets.Where(b => !b.Hidden);
}

public sealed record CvSkillGroup(string Name, IReadOnlyList<string> Skills, bool Hidden);

public sealed record CvEducation(string Degree, string Institution, string Period, IReadOnlyList<string> Notes);

public sealed record CvLanguage(string Name, string Level);

public sealed record CvLabels(string Profile, string Experience, string Skills, string Education, string Languages)
{
    public static CvLabels For(DocumentLanguage language) => language == DocumentLanguage.De
        ? new("PROFIL", "BERUFSERFAHRUNG", "KENNTNISSE", "AUSBILDUNG", "SPRACHEN")
        : new("PROFILE", "EXPERIENCE", "SKILLS", "EDUCATION", "LANGUAGES");
}

/// <summary>The CV variant for one vacancy: the profile's facts, selected and ordered — never reworded.</summary>
public sealed record CvDocument(
    DocumentLanguage Language,
    string Focus,
    IReadOnlyList<string> AvailableFocuses,
    string Name,
    string RoleLine,
    IReadOnlyList<CvContact> Contacts,
    string? Summary,
    IReadOnlyList<CvExperience> Experience,
    IReadOnlyList<CvSkillGroup> SkillGroups,
    IReadOnlyList<CvEducation> Education,
    IReadOnlyList<CvLanguage> Languages,
    CvLabels Labels,
    int UntranslatedTexts);

/// <summary>
/// Builds the CV variant deterministically (no AI): chooses the summary by the posting's focus (full stack, backend,
/// AI), adapts the role line from a few templates with my strongest matching skills, orders experience bullets and
/// skill groups by overlap with the posting and hides the weakest bullets beyond a per-entry limit. German documents
/// use the profile's <c>*De</c> texts; where one is missing the base text is used and counted
/// (<see cref="CvDocument.UntranslatedTexts"/>).
/// </summary>
public static class CvBuilder
{
    /// <summary>Visible bullets of the most recent entry; older entries get <see cref="MaxBulletsOlder"/>.</summary>
    public const int MaxBulletsFirst = 8;

    public const int MaxBulletsOlder = 3;

    private static readonly string[] DefaultGroupOrder = ["Backend", "Frontend", "Testing", "DevOps & Tools", "Data", "Weitere", "Other"];

    private static readonly TermMatcher AiTerms = new(["LLM", "LLMs", "GPT", "OpenAI", "Claude", "Machine Learning", "Künstliche Intelligenz", "Generative AI", "GenAI", "KI-Anwendungen", "KI-Lösungen", "AI-Anwendungen", "Prompt Engineering", "RAG"]);
    private static readonly TermMatcher AiShort = new(["KI", "AI", "ML"], matchCase: true);
    private static readonly TermMatcher FrontendTerms = new(["Frontend", "Front-End", "Vue", "Vue.js", "React", "Angular", "TypeScript", "JavaScript", "CSS", "Nuxt", "UI", "UX"]);
    private static readonly TermMatcher BackendTerms = new(["Backend", "Back-End", "C#", ".NET", "ASP.NET", "Java", "API", "APIs", "REST", "SQL", "Microservices", "Datenbank", "Datenbanken", "database", "server"]);
    private static readonly TermMatcher FullstackTerms = new(["Full Stack", "Fullstack", "Full-Stack-Entwickler"]);

    public static CvDocument Build(CandidateProfile profile, string title, string? description, DocumentLanguage language, CvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        options ??= CvOptions.Default;
        var de = language == DocumentLanguage.De;
        var terms = new VacancyTerms(profile, title, description);
        var untranslated = 0;

        var focuses = profile.Summary.Keys.Where(k => !k.EndsWith(".de", StringComparison.OrdinalIgnoreCase)).ToList();
        var focus = options.Focus is { } chosen && focuses.Contains(chosen) ? chosen : DetectFocus(title, description, focuses);
        string? summary = null;
        if (focus.Length > 0)
        {
            summary = Localized.Summary(profile, focus, language);
            if (de && !profile.Summary.ContainsKey($"{focus}.de"))
            {
                untranslated++;
            }
        }

        var personal = profile.Personal;
        var contacts = new List<CvContact> { new(personal.Email, $"mailto:{personal.Email}") };
        if (!string.IsNullOrWhiteSpace(personal.Phone))
        {
            contacts.Add(new(personal.Phone, null));
        }

        if (!string.IsNullOrWhiteSpace(personal.City))
        {
            contacts.Add(new(personal.City, null));
        }

        foreach (var url in new[] { personal.Website, personal.Linkedin, personal.Github })
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                contacts.Add(new(url.Contains("linkedin.", StringComparison.OrdinalIgnoreCase) ? "LinkedIn" : Links.Display(url), url));
            }
        }

        var hidden = options.HiddenBullets.ToHashSet(StringComparer.Ordinal);
        var shown = options.ShownBullets.ToHashSet(StringComparer.Ordinal);
        var order = options.BulletOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        var experience = new List<CvExperience>();
        foreach (var (entry, i) in profile.Experience.Select((e, i) => (e, i)))
        {
            if (de && entry.TitleDe is null)
            {
                untranslated++;
            }

            var bullets = entry.Bullets
                .Select((b, j) => (Id: $"exp{i}.b{j}", Bullet: b, Order: j, Score: terms.Score(b.Tags, b.Text)))
                .OrderBy(b => order.TryGetValue(b.Id, out var position) ? position : int.MaxValue)
                .ThenByDescending(b => order.ContainsKey(b.Id) ? 0 : b.Score)
                .ThenBy(b => b.Order)
                .ToList();
            var limit = i == 0 ? MaxBulletsFirst : MaxBulletsOlder;
            var result = new List<CvBullet>();
            var visible = 0;
            foreach (var b in bullets)
            {
                var autoHidden = !shown.Contains(b.Id) && !hidden.Contains(b.Id) && visible >= limit;
                var isHidden = hidden.Contains(b.Id) || autoHidden;
                if (!isHidden)
                {
                    visible++;
                    if (de && b.Bullet.TextDe is null)
                    {
                        untranslated++;
                    }
                }

                result.Add(new CvBullet(b.Id, de ? b.Bullet.TextDe ?? b.Bullet.Text : b.Bullet.Text, b.Score, isHidden, autoHidden));
            }

            var where = string.Join(", ", new[] { de ? entry.LocationDe ?? entry.Location : entry.Location, entry.Remote ? "remote" : null }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            experience.Add(new CvExperience(
                $"exp{i}", de ? entry.TitleDe ?? entry.Title : entry.Title, entry.Company, where.Length > 0 ? where : null,
                Dates.Period(entry.From, entry.To, language).Replace("seit ", "", StringComparison.Ordinal).Replace("since ", "", StringComparison.Ordinal)
                    + (entry.To is null ? (de ? " – heute" : " – present") : ""),
                result));
        }

        var groups = SkillGroups(profile, terms, language, options);

        var education = profile.Education.Select(e =>
        {
            if (de && e.DegreeDe is null)
            {
                untranslated++;
            }

            return new CvEducation(
                de ? e.DegreeDe ?? e.Degree : e.Degree,
                de ? e.InstitutionDe ?? e.Institution : e.Institution,
                Dates.Period(e.From, e.To, language),
                de && e.NotesDe.Count > 0 ? e.NotesDe : e.Notes);
        }).ToList();

        var languages = Languages(profile, de);

        return new CvDocument(
            language, focus, focuses, personal.Name, RoleLine(profile, terms, focus, language), contacts, summary,
            experience, groups, education, languages, CvLabels.For(language), untranslated);
    }

    /// <summary>„ai" when the posting is about AI/LLMs, „backend" without frontend terms, otherwise „fullstack".</summary>
    public static string DetectFocus(string title, string? description, IReadOnlyList<string> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        if (available.Count == 0)
        {
            return string.Empty;
        }

        var text = $"{title}\n{title}\n{description}";
        var ai = AiTerms.FindAll(text).Count() + AiShort.FindAll(text).Count();
        var front = FrontendTerms.FindAll(text).Select(f => f.Term).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var back = BackendTerms.FindAll(text).Select(f => f.Term).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var fullstackInTitle = FullstackTerms.FindFirst(title) is not null;

        string wanted = ai >= 3 ? "ai"
            : fullstackInTitle || (front >= 2 && back >= 1) ? "fullstack"
            : back > front ? "backend"
            : "fullstack";
        return available.Contains(wanted) ? wanted
            : available.Contains("fullstack") ? "fullstack"
            : available[0];
    }

    /// <summary>„Full-Stack-Entwickler · C# / .NET · TypeScript · Vue 3" — role by focus, then up to three skill blocks.</summary>
    public static string RoleLine(CandidateProfile profile, VacancyTerms terms, string focus, DocumentLanguage language)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(terms);
        var de = language == DocumentLanguage.De;
        var role = focus switch
        {
            "backend" => de ? "Backend-Entwickler" : "Backend Developer",
            "ai" => de ? "Softwareentwickler · KI-Anwendungen" : "Software Developer · AI Applications",
            _ => de ? "Full-Stack-Entwickler" : "Full-Stack Developer",
        };

        var blocks = SkillBlocks(profile, terms, focus);
        return blocks.Count == 0 ? role : $"{role} · {string.Join(" · ", blocks)}";
    }

    /// <summary>
    /// Up to three skill blocks for role line and e-mail text, from professionally used skills only: first the .NET
    /// block in profile order („C# / .NET"), then by focus the frontend skills (full stack, AI) or data skills
    /// (backend) — those the posting mentions first.
    /// </summary>
    public static IReadOnlyList<string> SkillBlocks(CandidateProfile profile, VacancyTerms terms, string focus)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(terms);
        var production = profile.Skills.Where(s => s.Level == SkillLevel.Production).ToList();
        bool InGroup(Skill s, string group) => string.Equals(s.Group, group, StringComparison.OrdinalIgnoreCase);

        var blocks = new List<string>();
        var dotnet = production.Where(s => InGroup(s, "dotnet")).Take(2).Select(s => s.Name).ToList();
        if (dotnet.Count > 0)
        {
            blocks.Add(string.Join(" / ", dotnet));
        }

        var second = focus == "backend" ? "data" : "frontend";
        blocks.AddRange(production
            .Select((s, i) => (Skill: s, Order: i))
            .Where(x => InGroup(x.Skill, second))
            .OrderByDescending(x => terms.Mentions(x.Skill))
            .ThenBy(x => x.Order)
            .Select(x => x.Skill.Name)
            .Take(3 - blocks.Count));
        return blocks;
    }

    private static List<CvSkillGroup> SkillGroups(CandidateProfile profile, VacancyTerms terms, DocumentLanguage language, CvOptions options)
    {
        var other = language == DocumentLanguage.De ? "Weitere" : "Other";
        var groups = profile.Skills
            .Select((s, i) => (Skill: s, Order: i))
            .Where(x => x.Skill.Level is SkillLevel.Production or SkillLevel.Project)
            .GroupBy(x => GroupName(x.Skill) ?? other, StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                Name: g.Key,
                Skills: g.OrderByDescending(x => terms.Mentions(x.Skill)).ThenBy(x => x.Order).Select(x => x.Skill.Name).ToList(),
                Mentioned: g.Count(x => terms.Mentions(x.Skill))))
            .ToList();

        var hidden = options.HiddenSkillGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = options.SkillGroupOrder.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i, StringComparer.OrdinalIgnoreCase);
        return groups
            .OrderBy(g => chosen.TryGetValue(g.Name, out var position) ? position : int.MaxValue)
            .ThenBy(g => g.Name == other ? 1 : 0)
            .ThenByDescending(g => g.Mentioned)
            .ThenBy(g => Array.IndexOf(DefaultGroupOrder, g.Name) is var index and >= 0 ? index : DefaultGroupOrder.Length)
            .Select(g => new CvSkillGroup(g.Name, g.Skills, hidden.Contains(g.Name)))
            .ToList();
    }

    /// <summary><c>cvGroup</c>, or a heading derived from the scoring group (dotnet/data → Backend, frontend → Frontend).</summary>
    private static string? GroupName(Skill skill) => skill.CvGroup ?? skill.Group?.ToLowerInvariant() switch
    {
        "dotnet" or "data" or "backend" => "Backend",
        "frontend" => "Frontend",
        _ => null,
    };

    /// <summary>One line per language; native languages share one line („Ukrainisch, Russisch — Muttersprache").</summary>
    private static List<CvLanguage> Languages(CandidateProfile profile, bool de)
    {
        var result = new List<CvLanguage>();
        foreach (var language in profile.Languages.Where(l => l.Level != LanguageLevel.Native))
        {
            var note = de ? language.NoteDe ?? language.Note : language.Note;
            result.Add(new CvLanguage(de ? language.NameDe ?? language.Name : language.Name,
                note is null ? language.Level.ToString()
                    : note.StartsWith('(') ? $"{language.Level} {note}"
                    : $"{language.Level}, {note}"));
        }

        var natives = profile.Languages.Where(l => l.Level == LanguageLevel.Native).Select(l => de ? l.NameDe ?? l.Name : l.Name).ToList();
        if (natives.Count > 0)
        {
            result.Add(new CvLanguage(string.Join(", ", natives), de ? "Muttersprache" : "Native"));
        }

        return result;
    }
}
