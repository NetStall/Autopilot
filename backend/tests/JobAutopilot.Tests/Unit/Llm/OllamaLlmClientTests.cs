using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using JobAutopilot.Core.Llm;
using JobAutopilot.Infrastructure.Llm;
using JobAutopilot.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Tests.Unit.Llm;

/// <summary>The Ollama client against scripted HTTP responses — never a real Ollama.</summary>
public sealed class OllamaLlmClientTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, JsonElement> Schema =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"type":"object","properties":{"a":{"type":"string"}}}""")!;

    private static readonly LlmRequest Request = new("Extraction", "extract", "extraction.v1", "System", "User", Schema);

    private readonly Handler _handler = new();
    private readonly HttpClient _http;

    public OllamaLlmClientTests() => _http = new HttpClient(_handler);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    private OllamaLlmClient Client(bool enabled = true, int retries = 0) => new(
        _http,
        Options.Create(new OllamaOptions { Enabled = enabled, BaseUrl = "http://ollama.test:11434", MaxRetries = retries }),
        NullLogger<OllamaLlmClient>.Instance,
        new TestTimeProvider(DateTimeOffset.UnixEpoch));

    private static string Completion(string content, string finish = "stop") => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { role = "assistant", content }, finish_reason = finish } },
        usage = new { prompt_tokens = 1200, completion_tokens = 300 },
    });

    [Fact]
    public async Task Sends_an_openai_compatible_request_with_the_schema_and_reads_json_and_usage()
    {
        _handler.Enqueue(HttpStatusCode.OK, Completion("""{"a":"b"}"""));

        var response = await Client().CompleteJsonAsync(Request, "qwen3:8b", Token);

        response.Should().Be(new LlmResponse("""{"a":"b"}""", new Core.Documents.TokenUsage(1200, 300), "qwen3:8b"));
        var (uri, body) = _handler.Requests.Single();
        uri.Should().Be("http://ollama.test:11434/v1/chat/completions");
        var json = JsonNode.Parse(body)!;
        json["model"]!.GetValue<string>().Should().Be("qwen3:8b");
        json["stream"]!.GetValue<bool>().Should().BeFalse();
        json["response_format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
        json["response_format"]!["json_schema"]!["schema"]!["type"]!.GetValue<string>().Should().Be("object");
        json["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()).Should().Equal("system", "user");
        json["reasoning_effort"].Should().BeNull("thinking is the model's default unless the request switches it off");
    }

    [Fact]
    public async Task Thinking_off_sends_reasoning_effort_none()
    {
        _handler.Enqueue(HttpStatusCode.OK, Completion("{}"));

        await Client().CompleteJsonAsync(Request with { Thinking = false }, "qwen3:8b", Token);

        JsonNode.Parse(_handler.Requests.Single().Body)!["reasoning_effort"]!.GetValue<string>().Should().Be("none");
    }

    [Fact]
    public async Task Switched_off_means_no_call()
    {
        var act = () => Client(enabled: false).CompleteJsonAsync(Request, "qwen3:8b", Token);

        (await act.Should().ThrowAsync<LlmException>()).Which.Error.Should().Be(LlmError.NotConfigured);
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_that_is_not_pulled_says_how_to_pull_it()
    {
        _handler.Enqueue(HttpStatusCode.NotFound, "{}");

        var act = () => Client().CompleteJsonAsync(Request, "qwen3:8b", Token);

        (await act.Should().ThrowAsync<LlmException>()).Which.Should().Match<LlmException>(e => e.Error == LlmError.NotConfigured && e.Message.Contains("ollama pull qwen3:8b"));
    }

    [Fact]
    public async Task Server_errors_are_retried_then_reported_as_unavailable()
    {
        _handler.Enqueue(HttpStatusCode.ServiceUnavailable, "{}");
        _handler.Enqueue(HttpStatusCode.OK, Completion("{}"));

        (await Client(retries: 1).CompleteJsonAsync(Request, "qwen3:8b", Token)).Json.Should().Be("{}");

        _handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        var act = () => Client().CompleteJsonAsync(Request, "qwen3:8b", Token);
        (await act.Should().ThrowAsync<LlmException>()).Which.Error.Should().Be(LlmError.Unavailable);
    }

    [Fact]
    public async Task Ollama_not_running_is_unavailable()
    {
        _handler.Refuse = true;

        var act = () => Client().CompleteJsonAsync(Request, "qwen3:8b", Token);

        (await act.Should().ThrowAsync<LlmException>()).Which.Error.Should().Be(LlmError.Unavailable);
    }

    [Fact]
    public async Task A_cut_off_answer_is_invalid()
    {
        _handler.Enqueue(HttpStatusCode.OK, Completion("{\"a\":", finish: "length"));

        var act = () => Client().CompleteJsonAsync(Request, "qwen3:8b", Token);

        (await act.Should().ThrowAsync<LlmException>()).Which.Error.Should().Be(LlmError.InvalidResponse);
    }

    [Fact]
    public async Task The_status_lists_pulled_models_or_says_unreachable()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"data":[{"id":"qwen3:8b"},{"id":"qwen3:14b"}]}""");

        var status = await Client().GetStatusAsync(Token);

        status.Reachable.Should().BeTrue();
        status.Models.Should().Equal("qwen3:14b", "qwen3:8b");
        _handler.Refuse = true;
        (await Client().GetStatusAsync(Token)).Reachable.Should().BeFalse();
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<(string Uri, string Body)> Requests { get; } = [];

        public bool Refuse { get; set; }

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Refuse)
            {
                throw new HttpRequestException("Connection refused");
            }

            Requests.Add((request.RequestUri!.ToString(), request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            var (status, body) = _responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
