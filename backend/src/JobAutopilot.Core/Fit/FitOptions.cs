namespace JobAutopilot.Core.Fit;

/// <summary>
/// Configuration section <c>Fit</c>: common requirements that are not (or not necessarily) in the profile, the words
/// that mark a requirement as optional, and the ranking penalties of <see cref="FitAnalyzer"/>.
/// </summary>
public sealed class FitOptions
{
    public const string SectionName = "Fit";

    /// <summary>E.g. Angular, React, Azure DevOps, Kubernetes, Führerschein — recognised even if the profile does not list them.</summary>
    public List<FitRequirementRule> CommonRequirements { get; init; } = [];

    /// <summary>A requirement in the same sentence as one of these is optional („von Vorteil", „nice to have").</summary>
    public List<string> OptionalMarkers { get; init; } = [];

    /// <summary>Headings of must-have sections („Was du mitbringst", „Dein Profil", „Anforderungen" …); empty = defaults.</summary>
    public List<string> MustHaveHeadings { get; init; } = [];

    /// <summary>Headings of nice-to-have sections („Ein Plus", „Wünschenswert", „Nice to have" …); empty = defaults.</summary>
    public List<string> NiceToHaveHeadings { get; init; } = [];

    /// <summary>Headings that end a requirements section („Deine Aufgaben", „Wir bieten", „Benefits" …); empty = defaults.</summary>
    public List<string> OtherHeadings { get; init; } = [];

    /// <summary>Headings of stack sections („Sprachen &amp; Technologien", „Tech-Stack") — likely required; empty = defaults.</summary>
    public List<string> StackHeadings { get; init; } = [];

    /// <summary>The dictionary of common technologies (<see cref="TechnologyCatalog"/>) finds requirements not in the profile.</summary>
    public bool UseTechnologyCatalog { get; init; } = true;

    /// <summary>Missing methods (DDD, CQRS, Event Sourcing, TDD …) at nice-to-have level together at most this much (07a2).</summary>
    public int MethodsGapMax { get; init; } = 6;

    /// <summary>Outside my places, not fully remote and a stated home-office share of at most this much = not reachable.</summary>
    public int ReachableFromHomeOfficePercent { get; init; } = 60;

    /// <summary>A requirement in the same sentence as one of these is a must-have („(Pflicht)", „zwingend"); empty = defaults.</summary>
    public List<string> MustHaveMarkers { get; init; } = [];

    /// <summary>Only seeds the Suchprofil's penalties on first start (step 05b); afterwards they are edited there.</summary>
    public FitPenalties Penalties { get; init; } = new();

    public static readonly string[] DefaultMustHaveHeadings =
    [
        "Was du mitbringst", "Das bringst du mit", "Was du mitbringen solltest", "Das solltest du mitbringen", "mitbringst",
        "mitbringen", "Dein Profil", "Ihr Profil", "Profil", "Anforderungen", "Voraussetzungen", "Qualifikation",
        "Qualifikationen", "Was wir erwarten", "Das erwarten wir", "Requirements", "Must have", "Must-have",
        "Your profile", "What you bring", "What you need", "Qualifications", "Who you are", "What You'll Bring",
        "What You’ll Bring", "What you will bring",
    ];

    public static readonly string[] DefaultNiceToHaveHeadings =
    [
        "Ein Plus", "kein Muss", "Wünschenswert", "von Vorteil", "idealerweise", "optional", "Nice to have", "Nice-to-have",
        "Bonus", "Pluspunkte", "Pluspunkt", "Gerne auch", "Was uns freut", "Nice to haves",
    ];

    public static readonly string[] DefaultOtherHeadings =
    [
        "Aufgaben", "Deine Aufgaben", "Ihre Aufgaben", "Was du bewirkst", "Was dich erwartet", "Das erwartet dich",
        "Was Sie erwartet", "Wir bieten", "Was wir bieten", "Unser Angebot", "Benefits", "Unsere Benefits", "Über uns",
        "Wer wir sind", "Das zeichnet dich aus", "Kontakt", "Bewerbung", "Your tasks", "What you will do",
        "Responsibilities", "We offer", "What we offer", "About us", "Perks",
    ];

    public static readonly string[] DefaultStackHeadings =
    [
        "Sprachen & Technologien", "Technologien", "Technologie-Stack", "Tech-Stack", "Tech Stack", "Techstack", "Unser Stack",
        "Unser Tech-Stack", "Tools & Technologien", "Tooling and stack", "Tooling & stack", "Our stack", "Our tech stack",
        "Technologies", "Tech stack and tools",
    ];

    public static readonly string[] DefaultMustHaveMarkers =
    [
        "Must-have", "Must have", "must", "(Pflicht)", "Pflicht", "zwingend", "unbedingt erforderlich", "Voraussetzung",
        "vorausgesetzt", "mindestens", "required",
    ];

    public IReadOnlyList<string> EffectiveMustHaveHeadings => MustHaveHeadings.Count > 0 ? MustHaveHeadings : DefaultMustHaveHeadings;

    public IReadOnlyList<string> EffectiveNiceToHaveHeadings => NiceToHaveHeadings.Count > 0 ? NiceToHaveHeadings : DefaultNiceToHaveHeadings;

    public IReadOnlyList<string> EffectiveStackHeadings => StackHeadings.Count > 0 ? StackHeadings : DefaultStackHeadings;

    public IReadOnlyList<string> EffectiveOtherHeadings => OtherHeadings.Count > 0 ? OtherHeadings : DefaultOtherHeadings;

    public IReadOnlyList<string> EffectiveMustHaveMarkers => MustHaveMarkers.Count > 0 ? MustHaveMarkers : DefaultMustHaveMarkers;
}

public sealed class FitRequirementRule
{
    public string Name { get; set; } = string.Empty;

    public List<string> Terms { get; init; } = [];

    /// <summary>Only a hint („Führerschein"), never a ranking penalty.</summary>
    public bool WarningOnly { get; set; }
}

/// <summary>Points subtracted from the relevance score for the ranking (<c>RankScore</c>).</summary>
public sealed class FitPenalties
{
    /// <summary>Per CEFR level the required German is above mine (C1 vs B1 = 2 levels).</summary>
    public int GermanPerLevel { get; set; } = 8;

    /// <summary>When more years are asked than I have and only „Senior" in the title says so.</summary>
    public int Experience { get; set; } = 8;

    /// <summary>… with a strong signal („langjährige Erfahrung", „5+ Jahre", „Lead", Senior in the requirements).</summary>
    public int ExperienceStrong { get; set; } = 10;

    /// <summary>Per gap in a must-have section („Was du mitbringst", „Anforderungen") or marked „Must-have".</summary>
    public int MustHaveGap { get; set; } = 15;

    /// <summary>Per gap required elsewhere (title, tasks, no section found), e.g. Angular as the main framework …</summary>
    public int RequiredGap { get; set; } = 6;

    /// <summary>Per gap in a stack section without a marker („Sprachen &amp; Technologien") — likely required.</summary>
    public int LikelyRequiredGap { get; set; } = 10;

    /// <summary>Per gap that is only nice to have („Ein Plus", „von Vorteil").</summary>
    public int NiceToHaveGap { get; set; } = 4;

    /// <summary>Must-have gaps up to this much in total.</summary>
    public int MustHaveGapMax { get; set; } = 30;

    /// <summary>All other gaps up to this much in total.</summary>
    public int RequiredGapMax { get; set; } = 18;

    /// <summary>Not fully remote and the location is outside <c>preferences.regions</c> (a hybrid job in Kiel).</summary>
    public int OutsideRegion { get; set; } = 15;

    /// <summary>… on site only or at most <see cref="FitOptions.ReachableFromHomeOfficePercent"/> home office: practically not reachable.</summary>
    public int Unreachable { get; set; } = 35;

    /// <summary>… with an unknown home-office share („anteilig Homeoffice"): visible, a moderate penalty.</summary>
    public int OutsideRegionUnknownShare { get; set; } = 20;

    /// <summary>Employer types listed in the profile's <c>preferences.avoidEmployerTypes</c> (e.g. Zeitarbeit).</summary>
    public int AvoidedEmployerType { get; set; } = 10;
}
