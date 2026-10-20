using JobAutopilot.Core.Llm;
using JobAutopilot.Infrastructure.Claude;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Infrastructure.Llm;

/// <summary>
/// Configuration section <c>Llm</c>: which provider and model each task uses, as <c>provider:model</c>, e.g.
/// <c>"Tasks": { "Letter": "claude:claude-sonnet-5-5" }</c>. A task without an entry runs on Claude with
/// <c>Claude:Model</c>. A local provider (step 07) only needs its own <see cref="ILlmClient"/> and an entry here.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public const string LetterTask = "Letter";

    public Dictionary<string, string> Tasks { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary><see cref="ILlmRouter"/> over the registered <see cref="ILlmClient"/>s and <see cref="LlmOptions"/>.</summary>
public sealed class LlmRouter(IEnumerable<ILlmClient> clients, IOptions<LlmOptions> options, IOptions<ClaudeOptions> claude) : ILlmRouter
{
    private readonly Dictionary<string, ILlmClient> _clients = clients.ToDictionary(c => c.Provider, StringComparer.OrdinalIgnoreCase);

    public string ModelFor(string task) => Resolve(task).Model;

    public Task<LlmResponse> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (provider, model) = Resolve(request.Task);
        return _clients.TryGetValue(provider, out var client)
            ? client.CompleteJsonAsync(request, model, cancellationToken)
            : throw new LlmException(LlmError.NotConfigured, $"Kein KI-Anbieter „{provider}“ eingerichtet (Llm:Tasks:{request.Task}).");
    }

    /// <summary><c>claude:claude-sonnet-5-5</c> → (claude, claude-sonnet-5-5); a bare model name means Claude.</summary>
    internal (string Provider, string Model) Resolve(string task)
    {
        if (options.Value.Tasks.TryGetValue(task, out var entry) && !string.IsNullOrWhiteSpace(entry))
        {
            var separator = entry.IndexOf(':', StringComparison.Ordinal);
            return separator > 0
                ? (entry[..separator].Trim(), entry[(separator + 1)..].Trim())
                : (ClaudeLlmClient.ProviderName, entry.Trim());
        }

        return (ClaudeLlmClient.ProviderName, claude.Value.Model);
    }
}
