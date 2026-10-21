using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Llm;

namespace JobAutopilot.Tests.Fakes;

/// <summary>
/// <see cref="ILlmClient"/> that answers from a script — every test uses this or a scripted HTTP handler, never a real
/// model. <see cref="Answer"/> gets the request and returns the JSON (or throws); calls are recorded.
/// </summary>
public sealed class FakeLlmClient(string provider = "ollama") : ILlmClient
{
    private readonly Lock _lock = new();

    public string Provider { get; } = provider;

    public bool IsEnabled { get; set; } = true;

    public Func<LlmRequest, string> Answer { get; set; } = _ => "{}";

    public List<LlmRequest> Requests { get; } = [];

    /// <summary>Awaited before each answer, e.g. to hold a call while a test cancels.</summary>
    public Func<CancellationToken, Task>? Before { get; set; }

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Requests.Add(request);
        }

        if (Before is { } before)
        {
            await before(cancellationToken);
        }

        if (!IsEnabled)
        {
            throw new LlmException(LlmError.NotConfigured, "aus");
        }

        return new LlmResponse(Answer(request), new TokenUsage(1000, 200), model);
    }
}

/// <summary>An HTTP handler that behaves like a service that is not running.</summary>
public sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("Connection refused (test)");
}
