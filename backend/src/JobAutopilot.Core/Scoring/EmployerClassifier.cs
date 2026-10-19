using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Scoring;

/// <summary>
/// Configuration section <c>EmployerDetection</c>: how recruitment agencies and temporary work are recognised
/// (phrase syntax of <see cref="TermMatcher"/>).
/// </summary>
public sealed class EmployerDetectionOptions
{
    public const string SectionName = "EmployerDetection";

    /// <summary>Company names of agencies, matched as whole words in the company field (e.g. „Hays", „FERCHAU").</summary>
    public List<string> KnownAgencies { get; init; } = [];

    /// <summary>Phrases in the description that mean temporary work (Arbeitnehmerüberlassung).</summary>
    public List<string> TemporaryPhrases { get; init; } = [];

    /// <summary>Phrases in the description that mean a recruiter writes for a client („im Auftrag unseres Kunden").</summary>
    public List<string> AgencyPhrases { get; init; } = [];

    /// <summary>
    /// Phrases only a direct employer writes, e.g. „keine Anfragen von Personaldienstleistern". They win over the
    /// agency and temporary phrases, which such a sentence would otherwise trigger.
    /// </summary>
    public List<string> DirectPhrases { get; init; } = [];
}

/// <summary>An employer type with the signal that decided it (for the reasons).</summary>
public sealed record EmployerClassification(EmployerType Type, string? Reason)
{
    public static readonly EmployerClassification None = new(EmployerType.Unknown, null);
}

/// <summary>
/// Decides the <see cref="EmployerType"/> of a vacancy. Order: the source's explicit flags (Arbeitsagentur
/// <c>istArbeitnehmerUeberlassung</c> / <c>istPrivateArbeitsvermittlung</c>), a known agency name, phrases in the
/// description (a direct employer's „keine Personaldienstleister" first), and only then the source's weak "posted by
/// the employer itself" (<see cref="EmployerType.Direct"/> hint) — many agencies post without setting the flags.
/// </summary>
public sealed class EmployerClassifier
{
    private readonly TermMatcher _knownAgencies;
    private readonly TermMatcher _temporaryPhrases;
    private readonly TermMatcher _agencyPhrases;
    private readonly TermMatcher _directPhrases;

    public EmployerClassifier(EmployerDetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _knownAgencies = new TermMatcher(options.KnownAgencies);
        _temporaryPhrases = new TermMatcher(options.TemporaryPhrases);
        _agencyPhrases = new TermMatcher(options.AgencyPhrases);
        _directPhrases = new TermMatcher(options.DirectPhrases);
    }

    public EmployerClassification Classify(string company, string? description, EmployerType sourceHint = EmployerType.Unknown)
    {
        ArgumentNullException.ThrowIfNull(company);

        switch (sourceHint)
        {
            case EmployerType.Temporary:
                return new(EmployerType.Temporary, "Zeitarbeit laut Arbeitsagentur (Arbeitnehmerüberlassung)");
            case EmployerType.Agency:
                return new(EmployerType.Agency, "Private Arbeitsvermittlung laut Arbeitsagentur");
        }

        if (_knownAgencies.FindFirst(company) is { } agency)
        {
            return new(EmployerType.Agency, $"Bekannter Personaldienstleister: {agency}");
        }

        if (_directPhrases.FindFirst(description) is { } direct)
        {
            return new(EmployerType.Direct, $"Direkter Arbeitgeber: „{direct}“");
        }

        if (_temporaryPhrases.FindFirst(description) is { } temporary)
        {
            return new(EmployerType.Temporary, $"Zeitarbeit: „{temporary}“");
        }

        if (_agencyPhrases.FindFirst(description) is { } phrase)
        {
            return new(EmployerType.Agency, $"Personalvermittlung: „{phrase}“");
        }

        return sourceHint == EmployerType.Direct
            ? new(EmployerType.Direct, "Direkter Arbeitgeber laut Arbeitsagentur")
            : EmployerClassification.None;
    }
}
