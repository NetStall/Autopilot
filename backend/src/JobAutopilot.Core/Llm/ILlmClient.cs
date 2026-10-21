using System.Text.Json;
using JobAutopilot.Core.Documents;

namespace JobAutopilot.Core.Llm;

/// <summary>
/// One structured call: a system and a user message, and the JSON schema the answer must follow.
/// <see cref="Task"/> names the job („Letter") so the router can pick provider and model;
/// <see cref="Operation"/> („generate", „rephrase") and <see cref="PromptVersion"/> only go to the log.
/// <see cref="Thinking"/> = false asks a reasoning model to answer directly (Ollama <c>reasoning_effort: none</c>).
/// </summary>
public sealed record LlmRequest(
    string Task,
    string Operation,
    string PromptVersion,
    string System,
    string User,
    IReadOnlyDictionary<string, JsonElement> JsonSchema,
    bool Thinking = true,
    int? MaxOutputTokens = null);

/// <summary>The answer as JSON text, with what it cost and which model gave it.</summary>
public sealed record LlmResponse(string Json, TokenUsage Usage, string Model);

/// <summary>
/// A language-model provider (Claude now; a local model in step 07). Implementations live in Infrastructure, keep
/// API keys to themselves, log only ids, model, prompt version, tokens and duration, and throw
/// <see cref="LlmException"/> for every failure the user should see.
/// </summary>
public interface ILlmClient
{
    /// <summary>The provider name used in <c>Llm:Tasks</c>, e.g. <c>claude</c>.</summary>
    string Provider { get; }

    /// <summary>Usable at all: Claude with an API key, Ollama switched on. Off = callers fall back to the rules.</summary>
    bool IsEnabled { get; }

    Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken);
}

/// <summary>Sends a request to the provider and model configured for its task (<c>Llm:Tasks:Letter = claude:&lt;model&gt;</c>).</summary>
public interface ILlmRouter
{
    /// <summary>The model a task runs on, e.g. for cost estimates; empty when the task is not configured.</summary>
    string ModelFor(string task);

    /// <summary>The task has an entry in <c>Llm:Tasks</c> and its provider is registered.</summary>
    bool IsConfigured(string task);

    Task<LlmResponse> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken);
}

public enum LlmError
{
    /// <summary>No API key or no provider configured.</summary>
    NotConfigured,

    /// <summary>Still 429 after the retries.</summary>
    RateLimited,

    /// <summary>5xx, network error.</summary>
    Unavailable,

    Timeout,

    /// <summary>The answer was not usable (empty, cut off at max tokens).</summary>
    InvalidResponse,

    /// <summary>The model declined.</summary>
    Refused,

    /// <summary>Rejected by the API (400/401/403 …): configuration problem.</summary>
    Rejected,
}

/// <summary>A failed model call. <see cref="Exception.Message"/> is German and safe to show.</summary>
public sealed class LlmException : Exception
{
    public LlmException()
    {
    }

    public LlmException(string message)
        : base(message)
    {
    }

    public LlmException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LlmException(LlmError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public LlmError Error { get; }
}
