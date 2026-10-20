namespace JobAutopilot.Core.Profile;

/// <summary>
/// Vitalii's profile — the only source of facts for everything generated later (AGENTS.md: „No invented facts").
/// Loaded from <c>data/profile.json</c> (git-ignored); <c>docs/profile.example.json</c> shows the format.
/// </summary>
public sealed class CandidateProfile
{
    public PersonalInfo Personal { get; init; } = new();

    /// <summary>
    /// Short self-descriptions by focus, e.g. <c>fullstack</c>, <c>backend</c>, <c>ai</c>; the German version of a
    /// focus under <c>fullstack.de</c> (used for German documents, step 06).
    /// </summary>
    public Dictionary<string, string> Summary { get; init; } = [];

    public List<ExperienceEntry> Experience { get; init; } = [];

    public List<Skill> Skills { get; init; } = [];

    public List<KnownGap> KnownGaps { get; init; } = [];

    public List<ProjectEntry> Projects { get; init; } = [];

    public List<EducationEntry> Education { get; init; } = [];

    public List<LanguageSkill> Languages { get; init; } = [];

    public List<Certificate> Certificates { get; init; } = [];

    public Preferences Preferences { get; init; } = new();
}

public sealed class PersonalInfo
{
    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string? Phone { get; init; }

    public string? City { get; init; }

    public string? Website { get; init; }

    public string? Github { get; init; }

    public string? Linkedin { get; init; }
}

public sealed class ExperienceEntry
{
    public string Company { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>German job title for German documents; <see cref="Title"/> otherwise.</summary>
    public string? TitleDe { get; init; }

    /// <summary><c>YYYY-MM</c>.</summary>
    public string From { get; init; } = string.Empty;

    /// <summary><c>YYYY-MM</c>; <c>null</c> = until today.</summary>
    public string? To { get; init; }

    public string? Location { get; init; }

    public string? LocationDe { get; init; }

    public bool Remote { get; init; }

    public List<Bullet> Bullets { get; init; } = [];
}

/// <summary>One achievement, with tags such as <c>csharp</c>, <c>vue</c>, <c>ci</c>, <c>leadership</c>, <c>migration</c>.</summary>
public sealed class Bullet
{
    public string Text { get; init; } = string.Empty;

    /// <summary>The same achievement in German (German CV); <see cref="Text"/> otherwise.</summary>
    public string? TextDe { get; init; }

    public List<string> Tags { get; init; } = [];
}

/// <summary>How a skill was used.</summary>
public enum SkillLevel
{
    /// <summary>Used professionally (paid work).</summary>
    Production,

    /// <summary>Own projects (portfolio, job-autopilot).</summary>
    Project,

    /// <summary>Learning right now / basics only.</summary>
    Learning,

    /// <summary>Not used — listed so that requirements are recognised as gaps.</summary>
    None,
}

public sealed class Skill
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Other spellings found in job ads, e.g. <c>.NET</c> ← <c>dotnet</c>, <c>ASP.NET Core</c>.</summary>
    public List<string> Aliases { get; init; } = [];

    public SkillLevel Level { get; init; }

    public double? Years { get; init; }

    /// <summary>Relevance group (e.g. <c>dotnet</c>, <c>frontend</c>) — shares the group's cap in the scoring.</summary>
    public string? Group { get; init; }

    /// <summary>Heading in the CV's skills section, e.g. <c>Backend</c>, <c>Frontend</c>, <c>Testing</c>, <c>DevOps &amp; Tools</c>.</summary>
    public string? CvGroup { get; init; }

    public IEnumerable<string> AllNames() => Aliases.Prepend(Name).Where(n => !string.IsNullOrWhiteSpace(n));
}

/// <summary>A skill that is missing, with an honest phrasing for letters — in German and English.</summary>
public sealed class KnownGap
{
    public string Skill { get; init; } = string.Empty;

    public List<string> Aliases { get; init; } = [];

    public string De { get; init; } = string.Empty;

    public string En { get; init; } = string.Empty;
}

public sealed class ProjectEntry
{
    public string Name { get; init; } = string.Empty;

    public string? Url { get; init; }

    public string Description { get; init; } = string.Empty;

    public string? DescriptionDe { get; init; }

    public List<string> Tags { get; init; } = [];
}

public sealed class EducationEntry
{
    public string Institution { get; init; } = string.Empty;

    public string? InstitutionDe { get; init; }

    public string Degree { get; init; } = string.Empty;

    public string? DegreeDe { get; init; }

    public string From { get; init; } = string.Empty;

    public string? To { get; init; }

    public List<string> Notes { get; init; } = [];

    /// <summary>German notes for German documents; replaces <see cref="Notes"/> when not empty.</summary>
    public List<string> NotesDe { get; init; } = [];
}

/// <summary>CEFR level: A1 … C2, or Native.</summary>
public enum LanguageLevel
{
    A1,
    A2,
    B1,
    B2,
    C1,
    C2,
    Native,
}

public sealed class LanguageSkill
{
    public string Name { get; init; } = string.Empty;

    public string? NameDe { get; init; }

    public LanguageLevel Level { get; init; }

    public string? Note { get; init; }

    public string? NoteDe { get; init; }
}

public sealed class Certificate
{
    public string Name { get; init; } = string.Empty;

    public string? Issuer { get; init; }

    public string? Date { get; init; }
}

public sealed class Preferences
{
    public int? SalaryMin { get; init; }

    public int? SalaryMax { get; init; }

    public List<string> Regions { get; init; } = [];

    public int? MaxCommuteMinutes { get; init; }

    /// <summary><c>Remote</c>, <c>Hybrid</c>, <c>OnSite</c> or <c>Any</c>.</summary>
    public string Remote { get; init; } = "Any";

    /// <summary><c>YYYY-MM-DD</c>.</summary>
    public string? EarliestStart { get; init; }

    /// <summary>Employer types to avoid, e.g. <c>Temporary</c>.</summary>
    public List<Vacancies.EmployerType> AvoidEmployerTypes { get; init; } = [];
}
