using System.Text.Json;
using System.Text.Json.Serialization;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Llm;
using JobAutopilot.Infrastructure.Claude;
using JobAutopilot.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Documents;

/// <summary>
/// <see cref="ILetterGenerator"/> on top of <see cref="ILlmRouter"/>: builds the versioned prompt (<see cref="LetterPrompt"/>),
/// asks the model configured for the task <c>Letter</c> (<c>Llm:Tasks:Letter</c>) for structured JSON and turns it into a
/// clean <see cref="LetterDraft"/>. Knows nothing about the provider; failures become <see cref="LetterGenerationException"/>.
/// </summary>
public sealed class LetterGenerator(ILlmRouter llm, IOptions<ClaudeOptions> options) : ILetterGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public string PromptVersion => Prompt.Name;

    private LetterPrompt Prompt => LetterPrompt.For(options.Value.LetterPromptVersion);

    public async Task<LetterGeneration> GenerateAsync(LetterInput input, CancellationToken cancellationToken, string? feedback = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var response = await CallAsync(
            feedback is null ? "generate" : "generate-retry", Prompt.GenerateMessage(input, options.Value.MaxVacancyCharacters, feedback), LetterPrompt.LetterSchema, cancellationToken);
        var letter = ParseLetter(response.Json, input, Prompt.Name != "letter.v1");
        return new LetterGeneration(letter, response.Usage, response.Model, Prompt.Name);
    }

    public async Task<ParagraphRephrasing> RephraseParagraphAsync(
        LetterInput input, LetterDraft current, int paragraph, string? hint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentOutOfRangeException.ThrowIfNegative(paragraph);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(paragraph, current.Paragraphs.Count);

        var response = await CallAsync(
            "rephrase",
            Prompt.RephraseMessage(input, current, paragraph, hint, options.Value.MaxVacancyCharacters),
            LetterPrompt.ParagraphSchema,
            cancellationToken);
        var result = Deserialize<ParagraphJson>(response.Json);
        var text = CleanParagraph(result.Paragraph);
        if (text.Length == 0)
        {
            throw Invalid("Der neue Absatz ist leer.");
        }

        return new ParagraphRephrasing(text, Clean(result.FactsUsed), Clean(result.GapsMentioned), response.Usage, response.Model, Prompt.Name);
    }

    private async Task<LlmResponse> CallAsync(
        string operation, string userMessage, IReadOnlyDictionary<string, JsonElement> schema, CancellationToken cancellationToken)
    {
        try
        {
            return await llm.CompleteJsonAsync(
                new LlmRequest(LlmOptions.LetterTask, operation, Prompt.Name, Prompt.System, userMessage, schema),
                cancellationToken);
        }
        catch (LlmException ex)
        {
            throw new LetterGenerationException(ToLetterError(ex.Error), ex.Message, ex);
        }
    }

    internal static LetterGenerationError ToLetterError(LlmError error) => error switch
    {
        LlmError.NotConfigured => LetterGenerationError.NotConfigured,
        LlmError.RateLimited => LetterGenerationError.RateLimited,
        LlmError.Unavailable => LetterGenerationError.Unavailable,
        LlmError.Timeout => LetterGenerationError.Timeout,
        LlmError.Refused => LetterGenerationError.Refused,
        LlmError.Rejected => LetterGenerationError.Rejected,
        _ => LetterGenerationError.InvalidResponse,
    };

    private static LetterDraft ParseLetter(string json, LetterInput input, bool salutationWithCompany)
    {
        var result = Deserialize<LetterJson>(json);
        var paragraphs = (result.Paragraphs ?? []).Select(CleanParagraph).Where(p => p.Length > 0).Take(LetterDraft.MaxParagraphs).ToList();
        if (paragraphs.Count == 0)
        {
            throw Invalid("Das Anschreiben enthält keine Absätze.");
        }

        var language = string.Equals(result.Language, "en", StringComparison.OrdinalIgnoreCase) ? DocumentLanguage.En : DocumentLanguage.De;
        var formality = string.Equals(result.Formality, "du", StringComparison.OrdinalIgnoreCase) ? Formality.Du : Formality.Sie;
        var known = input.Facts.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        return new LetterDraft(
            Line(result.Subject, $"Bewerbung als {input.Vacancy.Title}"),
            // letter.v2: the salutation with the company is set here, whatever the model wrote (06c).
            salutationWithCompany ? input.Tone.SalutationFor(input.Vacancy.Company) : Line(result.Salutation, input.Tone.Salutation),
            paragraphs,
            Line(result.Closing, language == DocumentLanguage.En ? "Kind regards" : "Mit freundlichen Grüßen"),
            language,
            formality,
            [.. Clean(result.FactsUsed).Where(known.Contains)],
            Clean(result.GapsMentioned));
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw Invalid("Die Antwort ist leer.");
        }
        catch (JsonException ex)
        {
            throw new LetterGenerationException(LetterGenerationError.InvalidResponse, "Die Antwort des Modells war kein gültiges JSON.", ex);
        }
    }

    /// <summary>Plain text: no line breaks inside a paragraph, no control characters, bounded length.</summary>
    private static string CleanParagraph(string? text)
    {
        var cleaned = string.Join(' ', (text ?? string.Empty).Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        cleaned = new string([.. cleaned.Where(c => !char.IsControl(c))]).Trim();
        return cleaned.Length > LetterDraft.MaxParagraphLength ? cleaned[..LetterDraft.MaxParagraphLength] : cleaned;
    }

    private static string Line(string? text, string fallback)
    {
        var cleaned = CleanParagraph(text);
        cleaned = cleaned.Length > LetterDraft.MaxLineLength ? cleaned[..LetterDraft.MaxLineLength] : cleaned;
        return cleaned.Length == 0 ? fallback : cleaned;
    }

    private static List<string> Clean(IEnumerable<string>? values) =>
        [.. (values ?? []).Select(v => CleanParagraph(v)).Where(v => v.Length is > 0 and <= 100).Distinct(StringComparer.Ordinal).Take(50)];

    private static LetterGenerationException Invalid(string message) => new(LetterGenerationError.InvalidResponse, message);

    private sealed class LetterJson
    {
        public string? Subject { get; init; }

        public string? Salutation { get; init; }

        public List<string>? Paragraphs { get; init; }

        public string? Closing { get; init; }

        public string? Language { get; init; }

        public string? Formality { get; init; }

        public List<string>? FactsUsed { get; init; }

        public List<string>? GapsMentioned { get; init; }
    }

    private sealed class ParagraphJson
    {
        public string? Paragraph { get; init; }

        public List<string>? FactsUsed { get; init; }

        public List<string>? GapsMentioned { get; init; }
    }
}
