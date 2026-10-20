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

    /// <summary>Only seeds the Suchprofil's penalties on first start (step 05b); afterwards they are edited there.</summary>
    public FitPenalties Penalties { get; init; } = new();
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

    /// <summary>When more years (or „Senior") are asked than I have.</summary>
    public int Experience { get; set; } = 8;

    /// <summary>Per required (not optional) gap, e.g. Angular as the main framework …</summary>
    public int RequiredGap { get; set; } = 6;

    /// <summary>… up to this much in total.</summary>
    public int RequiredGapMax { get; set; } = 18;

    /// <summary>Not fully remote and the location is outside <c>preferences.regions</c> (a hybrid job in Kiel).</summary>
    public int OutsideRegion { get; set; } = 15;

    /// <summary>Employer types listed in the profile's <c>preferences.avoidEmployerTypes</c> (e.g. Zeitarbeit).</summary>
    public int AvoidedEmployerType { get; set; } = 10;
}
