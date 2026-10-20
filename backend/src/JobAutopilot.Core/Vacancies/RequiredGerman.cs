namespace JobAutopilot.Core.Vacancies;

/// <summary>
/// The German a posting asks for, in the buckets of the list filter „Deutsch erforderlich". Ordered by strength:
/// when several phrases are found, the strongest wins.
/// </summary>
public enum RequiredGerman
{
    /// <summary>Nothing found.</summary>
    Unknown,

    /// <summary>German explicitly not required, or an English posting that mentions no German requirement.</summary>
    None,

    /// <summary>A1–B1, „Grundkenntnisse", „Deutschkenntnisse von Vorteil".</summary>
    B1OrLower,

    /// <summary>B2, „gute Deutschkenntnisse", „sicher auf Deutsch".</summary>
    B2,

    /// <summary>C1/C2, „verhandlungssicher", „sehr gute Deutschkenntnisse", „fließend", „Muttersprache".</summary>
    C1Plus,
}
