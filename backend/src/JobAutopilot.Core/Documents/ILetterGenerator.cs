namespace JobAutopilot.Core.Documents;

/// <summary>A generated letter with what the call cost.</summary>
public sealed record LetterGeneration(LetterDraft Letter, TokenUsage Usage, string Model, string PromptVersion);

/// <summary>One rewritten paragraph („Absatz neu formulieren").</summary>
public sealed record ParagraphRephrasing(
    string Paragraph,
    IReadOnlyList<string> FactsUsed,
    IReadOnlyList<string> GapsMentioned,
    TokenUsage Usage,
    string Model,
    string PromptVersion);

/// <summary>
/// Drafts a cover letter from <see cref="LetterInput"/> only (Claude API in Infrastructure; mocked in tests). The
/// result is data: plain text, checked by the <see cref="FactGuard"/> and reviewed by a human before anything leaves
/// the tool.
/// </summary>
public interface ILetterGenerator
{
    /// <summary>The prompt template version stored with each draft (e.g. <c>letter.v1</c>).</summary>
    string PromptVersion { get; }

    /// <param name="feedback">Review points of a first attempt („Die Anzeige duzt …") for one automatic retry (08a).</param>
    Task<LetterGeneration> GenerateAsync(LetterInput input, CancellationToken cancellationToken, string? feedback = null);

    /// <param name="paragraph">0-based index into <see cref="LetterDraft.Paragraphs"/>.</param>
    /// <param name="hint">Optional short wish, e.g. „kürzer"; treated like the rest of the input — as data.</param>
    Task<ParagraphRephrasing> RephraseParagraphAsync(
        LetterInput input, LetterDraft current, int paragraph, string? hint, CancellationToken cancellationToken);
}

public enum LetterGenerationError
{
    /// <summary>No API key configured.</summary>
    NotConfigured,

    /// <summary>Still 429 after the retries.</summary>
    RateLimited,

    /// <summary>5xx, network error.</summary>
    Unavailable,

    Timeout,

    /// <summary>The answer was not the expected JSON (or was cut off at max tokens).</summary>
    InvalidResponse,

    /// <summary>The model declined.</summary>
    Refused,

    /// <summary>Rejected by the API (400/401/403 …): configuration problem.</summary>
    Rejected,
}

/// <summary>A failed generation; nothing was stored. <see cref="Exception.Message"/> is German and safe to show.</summary>
public sealed class LetterGenerationException : Exception
{
    public LetterGenerationException()
    {
    }

    public LetterGenerationException(string message)
        : base(message)
    {
    }

    public LetterGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LetterGenerationException(LetterGenerationError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public LetterGenerationError Error { get; }
}
