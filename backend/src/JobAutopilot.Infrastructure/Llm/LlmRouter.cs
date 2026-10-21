using JobAutopilot.Core.Llm;
using JobAutopilot.Infrastructure.Claude;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Llm;

/// <summary>
/// Configuration section <c>Llm</c>: which provider and model each task uses, as <c>provider:model</c>, e.g.
/// <c>"Tasks": { "Letter": "claude:claude-sonnet-5-5", "Extraction": "ollama:qwen3:8b" }</c> — the only place a
/// model is configured. A task without an entry is not configured.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public const string LetterTask = "Letter";

    public const string ExtractionTask = "Extraction";

    /// <summary>„KI-Bewertung" 0–100 next to the rank (07b).</summary>
    public const string FitScoreTask = "FitScore";

    public Dictionary<string, string> Tasks { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary><see cref="ILlmRouter"/> over the registered <see cref="ILlmClient"/>s and <see cref="LlmOptions"/>.</summary>
public sealed class LlmRouter(IEnumerable<ILlmClient> clients, IOptions<LlmOptions> options) : ILlmRouter
{
    private readonly Dictionary<string, ILlmClient> _clients = clients.ToDictionary(c => c.Provider, StringComparer.OrdinalIgnoreCase);

    public string ModelFor(string task) => Resolve(task)?.Model ?? string.Empty;

    public bool IsConfigured(string task) =>
        Resolve(task) is { } target && _clients.TryGetValue(target.Provider, out var client) && client.IsEnabled;

    public Task<LlmResponse> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = Resolve(request.Task)
            ?? throw new LlmException(LlmError.NotConfigured, $"Für „{request.Task}“ ist kein Modell eingerichtet (Llm:Tasks:{request.Task}).");
        var (provider, model) = target;
        return _clients.TryGetValue(provider, out var client)
            ? client.CompleteJsonAsync(request, string.IsNullOrWhiteSpace(request.Model) ? model : request.Model.Trim(), cancellationToken)
            : throw new LlmException(LlmError.NotConfigured, $"Kein KI-Anbieter „{provider}“ eingerichtet (Llm:Tasks:{request.Task}).");
    }

    /// <summary>
    /// <c>claude:claude-sonnet-5-5</c> → (claude, claude-sonnet-5-5); <c>ollama:qwen3:8b</c> → (ollama, qwen3:8b) — the
    /// provider ends at the first colon; a bare model name means Claude. <c>null</c> without an entry.
    /// </summary>
    internal (string Provider, string Model)? Resolve(string task)
    {
        if (!options.Value.Tasks.TryGetValue(task, out var entry) || string.IsNullOrWhiteSpace(entry))
        {
            return null;
        }

        var separator = entry.IndexOf(':', StringComparison.Ordinal);
        return separator > 0
            ? (entry[..separator].Trim(), entry[(separator + 1)..].Trim())
            : (ClaudeLlmClient.ProviderName, entry.Trim());
    }
}
