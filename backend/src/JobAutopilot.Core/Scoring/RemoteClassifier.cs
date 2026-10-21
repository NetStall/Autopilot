using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Core.Scoring;

/// <summary>
/// Configuration section <c>Remote</c>: phrases that decide the <see cref="RemoteLevel"/> from title and description
/// (syntax of <see cref="TermMatcher"/>: whole words, flexible spaces/hyphens, <c>{n}</c> = a number). Checked in this
/// order: full remote, on site, hybrid — so „kein Homeoffice möglich" is on site, not hybrid.
/// </summary>
public sealed class RemoteOptions
{
    public const string SectionName = "Remote";

    public List<string> FullRemotePhrases { get; init; } = [];

    public List<string> OnSitePhrases { get; init; } = [];

    public List<string> HybridPhrases { get; init; } = [];

    /// <summary>Words that mean full remote when they appear in the title, e.g. „.NET Developer (Remote)".</summary>
    public List<string> FullRemoteTitleTerms { get; init; } = [];

    /// <summary>
    /// Phrases that make home office clearly partial (07b): „teilweise im Homeoffice", „hybrides Arbeiten im Headquarter",
    /// „Büro und Homeoffice". Unlike a bare „Homeoffice möglich" they mean: regularly in the office. Empty = defaults.
    /// </summary>
    public List<string> PartialPhrases { get; init; } = [];

    public static readonly string[] DefaultPartialPhrases =
    [
        "teilweise im Homeoffice", "teilweise im Home Office", "teilweise Homeoffice", "teilweise remote", "teilweise mobil",
        "teilweise mobiles Arbeiten", "hybrides Arbeiten im", "im Headquarter", "Büro und Homeoffice", "Büro und Mobile Office",
        "Mobile Office und Büro", "Homeoffice und Büro", "Präsenz im Büro", "regelmäßig vor Ort", "regelmäßig im Büro",
        "hybrid working", "hybrid work model", "days in the office", "office presence",
    ];

    public IReadOnlyList<string> EffectivePartialPhrases => PartialPhrases.Count > 0 ? PartialPhrases : DefaultPartialPhrases;
}

/// <summary>A remote level with the phrase that decided it (for the reasons).</summary>
public sealed record RemoteClassification(RemoteLevel Level, string? Reason)
{
    public static readonly RemoteClassification None = new(RemoteLevel.Unknown, null);
}

/// <summary>Derives the remote level from title and description with the phrases of <see cref="RemoteOptions"/>.</summary>
public sealed class RemoteClassifier
{
    private readonly TermMatcher _fullRemote;
    private readonly TermMatcher _onSite;
    private readonly TermMatcher _hybrid;
    private readonly TermMatcher _titleFullRemote;
    private readonly TermMatcher _partial;

    public RemoteClassifier(RemoteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _fullRemote = new TermMatcher(options.FullRemotePhrases);
        _onSite = new TermMatcher(options.OnSitePhrases);
        _hybrid = new TermMatcher(options.HybridPhrases);
        _titleFullRemote = new TermMatcher(options.FullRemoteTitleTerms);
        _partial = new TermMatcher(options.EffectivePartialPhrases);
    }

    /// <summary>A phrase that makes the home office clearly partial, or <c>null</c>.</summary>
    public string? FindPartial(string title, string? description) => _partial.FindFirst(title) ?? _partial.FindFirst(description);

    public RemoteClassification Classify(string title, string? description)
    {
        ArgumentNullException.ThrowIfNull(title);

        if ((_fullRemote.FindFirst(title) ?? _fullRemote.FindFirst(description)) is { } fullRemote)
        {
            return new(RemoteLevel.FullRemote, $"Voll remote: „{fullRemote}“");
        }

        // "… (hybrid)" in the title wins over the generic "Remote" title term.
        var hybridInTitle = _hybrid.FindFirst(title);
        if (hybridInTitle is null && _titleFullRemote.FindFirst(title) is { } titleTerm)
        {
            return new(RemoteLevel.FullRemote, $"Voll remote: „{titleTerm}“ im Titel");
        }

        if ((_onSite.FindFirst(title) ?? _onSite.FindFirst(description)) is { } onSite)
        {
            return new(RemoteLevel.OnSite, $"Vor Ort: „{onSite}“");
        }

        if ((hybridInTitle ?? _hybrid.FindFirst(description)) is { } hybrid)
        {
            return new(RemoteLevel.Hybrid, $"Hybrid: „{hybrid}“");
        }

        return RemoteClassification.None;
    }
}
