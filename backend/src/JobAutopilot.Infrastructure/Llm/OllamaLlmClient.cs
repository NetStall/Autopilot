using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Llm;

/// <summary>
/// Configuration section <c>Llm:Providers:Ollama</c>. Off by default: the app works without a local model and falls back
/// to the rules. From Docker the host's Ollama is <c>http://host.docker.internal:11434</c>.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Llm:Providers:Ollama";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Per attempt; a long German posting on a 8B model takes 10–60 s.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Extra attempts on 429/5xx/connection errors, with a growing pause (2 s, 4 s …).</summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>Deterministic extraction.</summary>
    public double Temperature { get; set; }
}

/// <summary>Reachability and pulled models, for <c>GET /api/llm/status</c>.</summary>
public sealed record OllamaStatus(bool Enabled, string BaseUrl, bool Reachable, IReadOnlyList<string> Models, string? Error);

/// <summary>
/// <see cref="ILlmClient"/> for a local Ollama through its OpenAI-compatible API (<c>/v1/chat/completions</c> with a JSON
/// schema as <c>response_format</c>) — a typed <see cref="HttpClient"/>, no SDK. Retries 429/5xx/connection errors,
/// one timeout per attempt, logs model, prompt version, tokens, duration and tokens/s — never prompts or answers.
/// </summary>
public sealed partial class OllamaLlmClient(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaLlmClient> logger, TimeProvider timeProvider)
    : ILlmClient
{
    public const string ProviderName = "ollama";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Provider => ProviderName;

    public bool IsEnabled => options.Value.Enabled;

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var settings = options.Value;
        if (!settings.Enabled)
        {
            throw new LlmException(LlmError.NotConfigured, "Ollama ist ausgeschaltet (Llm:Providers:Ollama:Enabled).");
        }

        var body = new ChatRequest(
            model,
            [new ChatMessage("system", request.System), new ChatMessage("user", request.User)],
            new ResponseFormat("json_schema", new JsonSchemaFormat("result", true, request.JsonSchema)),
            settings.Temperature,
            Stream: false,
            ReasoningEffort: request.Thinking ? null : "none",
            MaxTokens: request.MaxOutputTokens);

        var watch = Stopwatch.StartNew();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
                using var response = await httpClient.PostAsJsonAsync(Url(settings, "v1/chat/completions"), body, JsonOptions, timeout.Token);
                if (IsTransient(response.StatusCode) && attempt < settings.MaxRetries)
                {
                    await Task.Delay(Backoff(attempt), timeProvider, cancellationToken);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw Fail(request, model, response.StatusCode, watch, new LlmException(LlmError.NotConfigured,
                        $"Das Modell „{model}“ ist in Ollama nicht vorhanden (ollama pull {model})."));
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw Fail(request, model, response.StatusCode, watch, new LlmException(
                        IsTransient(response.StatusCode) ? LlmError.Unavailable : LlmError.Rejected,
                        $"Ollama hat die Anfrage abgelehnt ({(int)response.StatusCode})."));
                }

                var result = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, timeout.Token);
                return Read(request, model, result, watch);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException)
            {
                if (ex is HttpRequestException && attempt < settings.MaxRetries)
                {
                    await Task.Delay(Backoff(attempt), timeProvider, cancellationToken);
                    continue;
                }

                throw Fail(request, model, null, watch, ex is HttpRequestException
                    ? new LlmException(LlmError.Unavailable, $"Ollama ist unter {settings.BaseUrl} nicht erreichbar (läuft „ollama serve“?).", ex)
                    : new LlmException(LlmError.Timeout, $"Ollama hat nicht innerhalb von {settings.TimeoutSeconds} s geantwortet.", ex));
            }
            catch (JsonException ex)
            {
                throw Fail(request, model, null, watch, new LlmException(LlmError.InvalidResponse, "Die Antwort von Ollama war unerwartet.", ex));
            }
        }
    }

    /// <summary>Whether Ollama answers and which models are pulled (<c>GET /v1/models</c>).</summary>
    public async Task<OllamaStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var models = await httpClient.GetFromJsonAsync<ModelList>(Url(settings, "v1/models"), JsonOptions, timeout.Token);
            return new OllamaStatus(settings.Enabled, settings.BaseUrl, true, [.. (models?.Data ?? []).Select(m => m.Id).Order(StringComparer.Ordinal)], null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            return new OllamaStatus(settings.Enabled, settings.BaseUrl, false, [], ex is OperationCanceledException ? "Zeitüberschreitung" : "nicht erreichbar");
        }
    }

    private LlmResponse Read(LlmRequest request, string model, ChatResponse? result, Stopwatch watch)
    {
        var choice = result?.Choices is { Count: > 0 } choices ? choices[0] : null;
        var usage = new TokenUsage(result?.Usage?.PromptTokens ?? 0, result?.Usage?.CompletionTokens ?? 0);
        var seconds = watch.Elapsed.TotalSeconds;
        var tokensPerSecond = seconds > 0 ? (usage.OutputTokens / seconds).ToString("0.0", CultureInfo.InvariantCulture) : "-";
        var finish = choice?.FinishReason ?? "-";
        LogCall(logger, request.Operation, model, request.PromptVersion, usage.InputTokens, usage.OutputTokens, watch.ElapsedMilliseconds, tokensPerSecond, finish);
        if (string.Equals(choice?.FinishReason, "length", StringComparison.Ordinal))
        {
            throw new LlmException(LlmError.InvalidResponse, "Die Antwort von Ollama wurde abgeschnitten (Kontext zu klein? OLLAMA_CONTEXT_LENGTH).");
        }

        var text = choice?.Message?.Content;
        return string.IsNullOrWhiteSpace(text)
            ? throw new LlmException(LlmError.InvalidResponse, "Die Antwort von Ollama enthält keinen Text.")
            : new LlmResponse(text, usage, model);
    }

    private LlmException Fail(LlmRequest request, string model, HttpStatusCode? status, Stopwatch watch, LlmException error)
    {
        LogFailed(logger, request.Operation, model, status is { } code ? (int)code : 0, error.Error, watch.ElapsedMilliseconds);
        return error;
    }

    private static Uri Url(OllamaOptions settings, string path) => new(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), path);

    private static bool IsTransient(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(2 * (attempt + 1));

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Ollama {Operation}: model {Model}, prompt {PromptVersion}, {InputTokens} input / {OutputTokens} output tokens, {ElapsedMs} ms, {TokensPerSecond} tokens/s, finish {FinishReason}")]
    private static partial void LogCall(ILogger logger, string operation, string model, string promptVersion, int inputTokens, int outputTokens, long elapsedMs, string tokensPerSecond, string finishReason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ollama {Operation} failed: model {Model}, status {StatusCode}, {Error} after {ElapsedMs} ms")]
    private static partial void LogFailed(ILogger logger, string operation, string model, int statusCode, LlmError error, long elapsedMs);

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat,
        double Temperature,
        bool Stream,
        [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort = null,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens = null);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type, [property: JsonPropertyName("json_schema")] JsonSchemaFormat JsonSchema);

    private sealed record JsonSchemaFormat(string Name, bool Strict, IReadOnlyDictionary<string, JsonElement> Schema);

    private sealed record ChatResponse(IReadOnlyList<Choice>? Choices, ChatUsage? Usage);

    private sealed record Choice(ChatMessage? Message, [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);

    private sealed record ModelList(IReadOnlyList<ModelEntry>? Data);

    private sealed record ModelEntry(string Id);
}
