namespace JobAutopilot.Core.Vacancies;

/// <summary>How much of the work can be done from home. Ordered: a higher value means "more remote".</summary>
public enum RemoteLevel
{
    /// <summary>Nothing known.</summary>
    Unknown = 0,

    /// <summary>The posting explicitly rules out home office.</summary>
    OnSite = 1,

    /// <summary>Some home office (days per week, "nach Vereinbarung", mobiles Arbeiten).</summary>
    Hybrid = 2,

    /// <summary>Fully remote, e.g. "100 % remote", "deutschlandweit remote".</summary>
    FullRemote = 3,
}
