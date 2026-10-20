using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using JobAutopilot.Core.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Claude;

/// <summary>
/// <see cref="ILetterGenerator"/> with the official Anthropic C# SDK (ADR 0010) over an <see cref="HttpClient"/> from
/// <see cref="IHttpClientFactory"/>. Structured output (JSON schema) for the letter and for single paragraphs; the
/// SDK retries 429/5xx/connection errors with backoff. Logs only model, prompt version, token counts, duration and
/// status codes — never the letter, the profile or the posting.
/// </summary>
public sealed partial class ClaudeLetterGenerator : ILetterGenerator, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeLetterGenerator> _logger;
    private readonly AnthropicClient? _client;

    public ClaudeLetterGenerator(HttpClient httpClient, IOptions<ClaudeOptions> options, ILogger<ClaudeLetterGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
        if (_options.IsConfigured)
        {
            _client = new AnthropicClient(new Anthropic.Core.ClientOptions
            {
                HttpClient = httpClient,
                ApiKey = _options.ApiKey,
                BaseUrl = _options.BaseUrl,
                Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
                MaxRetries = _options.MaxRetries,
            });
        }
    }

    public string PromptVersion => LetterPrompt.Version;

    public async Task<LetterGeneration> GenerateAsync(LetterInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var prompt = LetterPrompt.Current;
        var (json, usage) = await CallAsync(
            "generate", prompt.GenerateMessage(input, _options.MaxVacancyCharacters), LetterPrompt.LetterSchema, cancellationToken);
        var letter = ParseLetter(json, input);
        return new LetterGeneration(letter, usage, _options.Model, LetterPrompt.Version);
    }

    public async Task<ParagraphRephrasing> RephraseParagraphAsync(
        LetterInput input, LetterDraft current, int paragraph, string? hint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentOutOfRangeException.ThrowIfNegative(paragraph);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(paragraph, current.Paragraphs.Count);

        var (json, usage) = await CallAsync(
            "rephrase",
            LetterPrompt.Current.RephraseMessage(input, current, paragraph, hint, _options.MaxVacancyCharacters),
            LetterPrompt.ParagraphSchema,
            cancellationToken);
        var result = Deserialize<ParagraphJson>(json);
        var text = CleanParagraph(result.Paragraph);
        if (text.Length == 0)
        {
            throw Invalid("Der neue Absatz ist leer.");
        }

        return new ParagraphRephrasing(text, Clean(result.FactsUsed), Clean(result.GapsMentioned), usage, _options.Model, LetterPrompt.Version);
    }

    public void Dispose() => _client?.Dispose();

    private async Task<(string Json, TokenUsage Usage)> CallAsync(
        string operation, string userMessage, IReadOnlyDictionary<string, JsonElement> schema, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new LetterGenerationException(LetterGenerationError.NotConfigured,
                "Kein Claude-API-Schlüssel konfiguriert (dotnet user-secrets set \"Claude:ApiKey\" …).");
        }

        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = LetterPrompt.Current.System,
            Messages = [new() { Role = Role.User, Content = userMessage }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = schema },
                Effort = string.IsNullOrWhiteSpace(_options.Effort) ? null : ParseEffort(_options.Effort),
            },
        };
        if (_options.Temperature is { } temperature)
        {
            // Deprecated for current models (only 1.0 is accepted); kept for older models configured on purpose.
#pragma warning disable CS0618
            parameters = parameters with { Temperature = temperature };
#pragma warning restore CS0618
        }

        var watch = Stopwatch.StartNew();
        Message message;
        try
        {
            message = await _client.Messages.Create(parameters, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (Map(ex) is { } mapped)
        {
            LogFailed(_logger, operation, _options.Model, (ex as AnthropicApiException)?.StatusCode is { } status ? (int)status : 0,
                mapped.Error, watch.ElapsedMilliseconds);
            throw mapped;
        }

        var usage = new TokenUsage((int)message.Usage.InputTokens, (int)message.Usage.OutputTokens);
        StopReason? stop = message.StopReason is { } reason ? (StopReason)reason : null;
        var stopText = stop?.ToString() ?? "-";
        LogCall(_logger, operation, _options.Model, LetterPrompt.Version, usage.InputTokens, usage.OutputTokens, watch.ElapsedMilliseconds, stopText);
        if (stop == StopReason.Refusal)
        {
            throw new LetterGenerationException(LetterGenerationError.Refused, "Das Modell hat die Anfrage abgelehnt.");
        }

        if (stop == StopReason.MaxTokens)
        {
            throw Invalid($"Die Antwort wurde bei {_options.MaxTokens} Tokens abgeschnitten (Claude:MaxTokens erhöhen).");
        }

        var text = message.Content.Select(b => b.TryPickText(out var t) ? t.Text : null).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        return text is null ? throw Invalid("Die Antwort enthält keinen Text.") : (text, usage);
    }

    private static Effort ParseEffort(string value) => value switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };

    private static LetterGenerationException? Map(Exception ex) => ex switch
    {
        AnthropicRateLimitException e => new(LetterGenerationError.RateLimited,
            "Claude ist gerade ausgelastet (429). Bitte in einer Minute erneut versuchen.", e),
        Anthropic5xxException e => new(LetterGenerationError.Unavailable,
            "Claude ist gerade nicht erreichbar (Serverfehler). Bitte später erneut versuchen.", e),
        AnthropicApiException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } e => new(LetterGenerationError.Rejected,
            "Der Claude-API-Schlüssel wurde abgelehnt.", e),
        AnthropicApiException e => new(LetterGenerationError.Rejected,
            $"Claude hat die Anfrage abgelehnt ({(int)e.StatusCode}) – Konfiguration prüfen (Modell, Temperature).", e),
        TimeoutException or TaskCanceledException => new(LetterGenerationError.Timeout,
            "Claude hat nicht rechtzeitig geantwortet (Zeitüberschreitung).", ex),
        AnthropicIOException or HttpRequestException => new(LetterGenerationError.Unavailable,
            "Claude ist gerade nicht erreichbar (Netzwerkfehler).", ex),
        AnthropicException e => new(LetterGenerationError.InvalidResponse, "Die Antwort von Claude war unerwartet.", e),
        _ => null,
    };

    private static LetterDraft ParseLetter(string json, LetterInput input)
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
            Line(result.Salutation, input.Tone.Salutation),
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
            throw new LetterGenerationException(LetterGenerationError.InvalidResponse, "Die Antwort von Claude war kein gültiges JSON.", ex);
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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Claude {Operation}: model {Model}, prompt {PromptVersion}, {InputTokens} input / {OutputTokens} output tokens, {ElapsedMs} ms, stop {StopReason}")]
    private static partial void LogCall(ILogger logger, string operation, string model, string promptVersion, int inputTokens, int outputTokens, long elapsedMs, string stopReason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude {Operation} failed: model {Model}, status {StatusCode}, {Error} after {ElapsedMs} ms")]
    private static partial void LogFailed(ILogger logger, string operation, string model, int statusCode, LetterGenerationError error, long elapsedMs);

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
