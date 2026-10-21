using JobAutopilot.Core.Llm;
using JobAutopilot.Infrastructure.Llm;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Api.Features.Llm;

/// <summary>One task of <c>Llm:Tasks</c>: where it runs and whether it can run now.</summary>
public sealed record LlmTaskDto(string Task, string Target, bool Ready);

/// <summary>For <c>GET /api/llm/status</c>: the local model's reachability and pulled models, the task mapping.</summary>
public sealed record LlmStatusDto(OllamaStatus Ollama, bool ClaudeConfigured, IReadOnlyList<LlmTaskDto> Tasks);

public static class LlmEndpoints
{
    public static IEndpointRouteBuilder MapLlmEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/llm/status", GetStatus).WithTags("LLM").WithName(nameof(GetStatus));
        return app;
    }

    /// <summary>Never shows a key — only whether one is configured.</summary>
    private static async Task<Ok<LlmStatusDto>> GetStatus(
        OllamaLlmClient ollama,
        IEnumerable<ILlmClient> clients,
        ILlmRouter router,
        IOptions<LlmOptions> options,
        CancellationToken cancellationToken)
    {
        var status = await ollama.GetStatusAsync(cancellationToken);
        var claude = clients.FirstOrDefault(c => c.Provider == Infrastructure.Claude.ClaudeLlmClient.ProviderName)?.IsEnabled ?? false;
        var tasks = options.Value.Tasks
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => new LlmTaskDto(t.Key, t.Value, router.IsConfigured(t.Key)))
            .ToList();
        return TypedResults.Ok(new LlmStatusDto(status, claude, tasks));
    }
}
