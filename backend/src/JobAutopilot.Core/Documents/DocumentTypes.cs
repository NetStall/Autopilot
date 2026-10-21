namespace JobAutopilot.Core.Documents;

public enum DocumentLanguage
{
    De,
    En,
}

/// <summary>How the employer addresses applicants (German postings): „Sie" or „du".</summary>
public enum Formality
{
    Sie,
    Du,
}

/// <summary>
/// The cover letter as structured data (also the structured output of the model). Plain text only — it is never
/// rendered as HTML. <see cref="FactsUsed"/> are ids of <see cref="LetterFact"/>s, <see cref="GapsMentioned"/> skill
/// names of known gaps the letter is honest about.
/// </summary>
public sealed record LetterDraft(
    string Subject,
    string Salutation,
    IReadOnlyList<string> Paragraphs,
    string Closing,
    DocumentLanguage Language,
    Formality Formality,
    IReadOnlyList<string> FactsUsed,
    IReadOnlyList<string> GapsMentioned)
{
    public const int MaxParagraphs = 8;

    public const int MaxParagraphLength = 2000;

    public const int MaxLineLength = 300;

    public IEnumerable<string> AllText() => Paragraphs.Prepend(Salutation).Prepend(Subject).Append(Closing);
}

public enum WarningSeverity
{
    /// <summary>Probably wrong (invented fact) — approving needs an explicit confirmation.</summary>
    Warning,

    /// <summary>A style hint.</summary>
    Hint,
}

/// <summary>A finding of the fact guard or the CV check, shown in the review; <see cref="Paragraph"/> is 0-based.</summary>
public sealed record DraftWarning(string Code, string Message, int? Paragraph = null, WarningSeverity Severity = WarningSeverity.Warning)
{
    public const string InventedTechnology = "invented-technology";
    public const string LearningAsExperience = "learning-as-experience";
    public const string InventedNumber = "invented-number";
    public const string LanguageAboveProfile = "language-above-profile";
    public const string MissingGap = "missing-gap";
    public const string MixedFormality = "mixed-formality";
    public const string FormalityMismatch = "formality-mismatch";
    public const string LanguageMismatch = "language-mismatch";
    public const string TooLong = "too-long";
    public const string MissingWebsite = "missing-website";
    public const string Cliche = "cliche";
    public const string CvUntranslated = "cv-untranslated";
    public const string CvTooLong = "cv-too-long";
}

/// <summary>Tokens of one or more model calls.</summary>
public sealed record TokenUsage(int InputTokens, int OutputTokens)
{
    public static readonly TokenUsage None = new(0, 0);

    public int Total => InputTokens + OutputTokens;

    public TokenUsage Add(TokenUsage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(InputTokens + other.InputTokens, OutputTokens + other.OutputTokens);
    }
}
