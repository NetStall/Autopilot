using System.Text.RegularExpressions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Filters;

/// <summary>Why a vacancy is filtered (layer 1, 08b) — hidden by default, never deleted.</summary>
public enum HardFilterReason
{
    Unreachable,
    GermanC1,
    RoleType,
    Stack,
}

/// <summary>
/// „Harte Filter" of the Suchprofil (08b): cheap rules before any model call, each with on/off and its lists. Defaults
/// apply to a saved Suchprofil without this block.
/// </summary>
public sealed class HardFilterSettings
{
    public static readonly string[] DefaultRoleTerms =
    [
        "Werkstudent", "Werkstudentin", "Working Student", "Praktikum", "Praktikant", "Praktikantin", "Internship", "Intern",
        "Ausbildung", "Auszubildende", "Azubi", "Duales Studium", "Dualer Student", "Duale Studentin", "Abschlussarbeit",
        "Bachelorarbeit", "Masterarbeit", "Thesis", "Teamleitung", "Teamleiter", "Teamleiterin", "Head of", "Principal", "Lead",
    ];

    public static readonly string[] DefaultCoreSkills = ["C#", ".NET", "ASP.NET", "TypeScript", "JavaScript", "Vue", "Vue.js", "Nuxt"];

    public static readonly string[] DefaultMismatchTitleTerms =
        ["Embedded", "C++", "C/C++", "Firmware", "SAP", "ABAP", "Java", "PHP", "SPS"];

    /// <summary>German C1 as an explicit requirement („C1", „verhandlungssicher", „Muttersprache", „fließend"); off = „C1-Stellen trotzdem zeigen".</summary>
    public bool GermanC1 { get; set; } = true;

    public bool RoleType { get; set; } = true;

    public List<string> RoleTerms { get; set; } = [.. DefaultRoleTerms];

    public bool Stack { get; set; } = true;

    /// <summary>None of these anywhere in title + description → filtered.</summary>
    public List<string> CoreSkills { get; set; } = [.. DefaultCoreSkills];

    /// <summary>A title with one of these and none of <see cref="CoreSkills"/> → filtered („Java Developer", „SAP ABAP Entwickler").</summary>
    public List<string> MismatchTitleTerms { get; set; } = [.. DefaultMismatchTitleTerms];
}

public sealed record HardFilterResult(HardFilterReason Reason, string Note);

/// <summary>
/// Layer 1 of the pipeline (08b): the first matching filter wins — role type, stack, German C1, reachability. A vacancy
/// with an application or my explicit „Passt" is never filtered; that is checked where the list is queried.
/// </summary>
public static partial class HardFilters
{
    /// <param name="hideUnreachable">„Erreichbarkeit" — the Suchprofil's existing „Nicht erreichbare ausblenden" (same semantics).</param>
    public static HardFilterResult? Check(Vacancy vacancy, HardFilterSettings settings, bool hideUnreachable)
    {
        ArgumentNullException.ThrowIfNull(vacancy);
        ArgumentNullException.ThrowIfNull(settings);
        var title = vacancy.Title ?? string.Empty;
        var text = $"{title}\n{vacancy.Description}";

        // An empty list means the default list (the Suchprofil form may be cleared).
        static IReadOnlyList<string> Or(List<string>? list, string[] defaults) => list is { Count: > 0 } ? list : defaults;
        if (settings.RoleType && new TermMatcher(Or(settings.RoleTerms, HardFilterSettings.DefaultRoleTerms)).FindFirst(title) is { } role)
        {
            return new(HardFilterReason.RoleType, $"Rolle: „{role}“");
        }

        if (settings.Stack)
        {
            // Without a description the stack is unknown: only the title rule below applies.
            var core = new TermMatcher(Or(settings.CoreSkills, HardFilterSettings.DefaultCoreSkills));
            if (!string.IsNullOrWhiteSpace(vacancy.Description) && core.FindFirst(text) is null)
            {
                return new(HardFilterReason.Stack, "keiner meiner Kern-Skills in der Anzeige");
            }

            if (core.FindFirst(title) is null && new TermMatcher(Or(settings.MismatchTitleTerms, HardFilterSettings.DefaultMismatchTitleTerms), matchCase: true).FindFirst(title) is { } other)
            {
                return new(HardFilterReason.Stack, $"anderer Stack im Titel: „{other}“");
            }
        }

        if (settings.GermanC1 && GermanRequirementDetector.Detect(title, vacancy.Description) is { Category: RequiredGerman.C1Plus, Phrase: { } phrase }
            && StrictGerman().IsMatch(phrase))
        {
            return new(HardFilterReason.GermanC1, $"Deutsch C1 („{phrase}“)");
        }

        if (hideUnreachable && vacancy.Unreachable)
        {
            return new(HardFilterReason.Unreachable, "nicht erreichbar");
        }

        return null;
    }

    public static string Label(HardFilterReason reason) => reason switch
    {
        HardFilterReason.Unreachable => "nicht erreichbar",
        HardFilterReason.GermanC1 => "Deutsch C1",
        HardFilterReason.RoleType => "Rolle",
        _ => "Stack",
    };

    /// <summary>Only explicit wording filters; „sehr gute Deutschkenntnisse" (C1 for the rank) stays visible.</summary>
    [GeneratedRegex(@"\b(?:C1|C2)\b|verhandlungssicher|mutterspr|native|fließend|fliessend|fluent", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex StrictGerman();
}
