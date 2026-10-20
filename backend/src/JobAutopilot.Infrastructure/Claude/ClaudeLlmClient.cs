using System.Diagnostics;
using System.Net;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Claude;

/// <summary>
/// <see cref="ILlmClient"/> for Claude with the official Anthropic C# SDK (ADR 0010) over an <see cref="HttpClient"/>
/// from <see cref="IHttpClientFactory"/>. Structured output (JSON schema); the SDK retries 429/5xx/connection errors
/// with backoff (<c>Claude:MaxRetries</c>) and applies <c>Claude:TimeoutSeconds</c>. Logs only operation, model,
/// prompt version, token counts, duration and status codes — never prompts, answers or the API key.
/// </summary>
public sealed partial class ClaudeLlmClient : ILlmClient, IDisposable
{
    public const string ProviderName = "claude";

    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeLlmClient> _logger;
    private readonly AnthropicClient? _client;

    public ClaudeLlmClient(HttpClient httpClient, IOptions<ClaudeOptions> options, ILogger<ClaudeLlmClient> logger)
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

    public string Provider => ProviderName;

    public void Dispose() => _client?.Dispose();

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (_client is null)
        {
            throw new LlmException(LlmError.NotConfigured,
                "Kein Claude-API-Schlüssel konfiguriert (dotnet user-secrets set \"Claude:ApiKey\" …).");
        }

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = _options.MaxTokens,
            System = request.System,
            Messages = [new() { Role = Role.User, Content = request.User }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = request.JsonSchema },
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
            LogFailed(_logger, request.Operation, model, (ex as AnthropicApiException)?.StatusCode is { } status ? (int)status : 0,
                mapped.Error, watch.ElapsedMilliseconds);
            throw mapped;
        }

        var usage = new TokenUsage((int)message.Usage.InputTokens, (int)message.Usage.OutputTokens);
        StopReason? stop = message.StopReason is { } reason ? (StopReason)reason : null;
        var stopText = stop?.ToString() ?? "-";
        LogCall(_logger, request.Operation, model, request.PromptVersion, usage.InputTokens, usage.OutputTokens, watch.ElapsedMilliseconds, stopText);
        if (stop == StopReason.Refusal)
        {
            throw new LlmException(LlmError.Refused, "Das Modell hat die Anfrage abgelehnt.");
        }

        if (stop == StopReason.MaxTokens)
        {
            throw new LlmException(LlmError.InvalidResponse, $"Die Antwort wurde bei {_options.MaxTokens} Tokens abgeschnitten (Claude:MaxTokens erhöhen).");
        }

        var text = message.Content.Select(b => b.TryPickText(out var t) ? t.Text : null).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        return text is null
            ? throw new LlmException(LlmError.InvalidResponse, "Die Antwort enthält keinen Text.")
            : new LlmResponse(text, usage, model);
    }

    private static Effort ParseEffort(string value) => value switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };

    private static LlmException? Map(Exception ex) => ex switch
    {
        AnthropicRateLimitException e => new(LlmError.RateLimited,
            "Claude ist gerade ausgelastet (429). Bitte in einer Minute erneut versuchen.", e),
        Anthropic5xxException e => new(LlmError.Unavailable,
            "Claude ist gerade nicht erreichbar (Serverfehler). Bitte später erneut versuchen.", e),
        AnthropicApiException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } e => new(LlmError.Rejected,
            "Der Claude-API-Schlüssel wurde abgelehnt.", e),
        AnthropicApiException e => new(LlmError.Rejected,
            $"Claude hat die Anfrage abgelehnt ({(int)e.StatusCode}) – Konfiguration prüfen (Modell, Temperature).", e),
        TimeoutException or TaskCanceledException => new(LlmError.Timeout,
            "Claude hat nicht rechtzeitig geantwortet (Zeitüberschreitung).", ex),
        AnthropicIOException or HttpRequestException => new(LlmError.Unavailable,
            "Claude ist gerade nicht erreichbar (Netzwerkfehler).", ex),
        AnthropicException e => new(LlmError.InvalidResponse, "Die Antwort von Claude war unerwartet.", e),
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Claude {Operation}: model {Model}, prompt {PromptVersion}, {InputTokens} input / {OutputTokens} output tokens, {ElapsedMs} ms, stop {StopReason}")]
    private static partial void LogCall(ILogger logger, string operation, string model, string promptVersion, int inputTokens, int outputTokens, long elapsedMs, string stopReason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude {Operation} failed: model {Model}, status {StatusCode}, {Error} after {ElapsedMs} ms")]
    private static partial void LogFailed(ILogger logger, string operation, string model, int statusCode, LlmError error, long elapsedMs);
}
