using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using JobAutopilot.Core.Documents;
using JobAutopilot.Infrastructure.Claude;
using JobAutopilot.Infrastructure.Documents;
using JobAutopilot.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Tests.Unit.Documents;

/// <summary>The letter generator with the Claude client against scripted HTTP responses — never the real API.</summary>
public sealed class LetterGeneratorTests : IDisposable
{
    private static readonly string[] Gaps = ["Angular"];

    private readonly ScriptedHandler _handler = new();
    private readonly HttpClient _http;
    private readonly List<ClaudeLlmClient> _clients = [];

    public LetterGeneratorTests() => _http = new HttpClient(_handler);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _clients.ForEach(c => c.Dispose());
        _http.Dispose();
        _handler.Dispose();
    }

    private LetterGenerator Generator(Action<ClaudeOptions>? configure = null, LlmOptions? llm = null)
    {
        var options = new ClaudeOptions { ApiKey = "test-key", BaseUrl = "https://claude.test", MaxRetries = 0, TimeoutSeconds = 30 };
        configure?.Invoke(options);
        var client = new ClaudeLlmClient(_http, Options.Create(options), NullLogger<ClaudeLlmClient>.Instance);
        _clients.Add(client);
        llm ??= new LlmOptions { Tasks = { ["Letter"] = "claude:claude-sonnet-5-5" } };
        return new LetterGenerator(new LlmRouter([client], Options.Create(llm)), Options.Create(options));
    }

    private static string LetterJson(string? subject = null, string? paragraph = null, string[]? facts = null) => JsonSerializer.Serialize(new
    {
        subject = subject ?? "Bewerbung als Full-Stack-Entwickler (m/w/d)",
        salutation = "Sehr geehrte Damen und Herren,",
        paragraphs = new[] { paragraph ?? "Absatz eins mit example.org.", "Absatz zwei.\nmit Zeilenumbruch", "Ganz offen: Angular kenne ich noch nicht." },
        closing = "Mit freundlichen Grüßen",
        language = "de",
        formality = "sie",
        factsUsed = facts ?? ["personal.website", "exp0.b0", "invented.id"],
        gapsMentioned = Gaps,
    });

    private static string MessageResponse(string text, string stopReason = "end_turn") => JsonSerializer.Serialize(new
    {
        id = "msg_test",
        type = "message",
        role = "assistant",
        model = "claude-sonnet-5-5",
        content = new object[] { new { type = "text", text } },
        stop_reason = stopReason,
        stop_sequence = (string?)null,
        usage = new { input_tokens = 4321, output_tokens = 987 },
    });

    [Fact]
    public async Task Generates_a_letter_with_structured_output_and_counts_tokens()
    {
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse(LetterJson()));
        var generator = Generator();

        var result = await generator.GenerateAsync(DocumentTestData.Input(), Token);

        result.Letter.Subject.Should().Be("Bewerbung als Full-Stack-Entwickler (m/w/d)");
        result.Letter.Paragraphs.Should().HaveCount(3);
        result.Letter.Paragraphs[1].Should().Be("Absatz zwei. mit Zeilenumbruch", "paragraphs are plain text without inner line breaks");
        result.Letter.FactsUsed.Should().Equal(["personal.website", "exp0.b0"], "unknown fact ids are dropped");
        result.Letter.Formality.Should().Be(Formality.Sie);
        result.Usage.Should().Be(new TokenUsage(4321, 987));
        result.Model.Should().Be("claude-sonnet-5-5");
        result.PromptVersion.Should().Be("letter.v1");
    }

    [Fact]
    public async Task The_task_mapping_picks_provider_and_model()
    {
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse(LetterJson()));
        var llm = new LlmOptions { Tasks = { ["Letter"] = "claude:claude-mapped-model" } };
        var generator = Generator(llm: llm);

        var result = await generator.GenerateAsync(DocumentTestData.Input(), Token);

        JsonNode.Parse(_handler.Requests.Single().Body)!["model"]!.GetValue<string>().Should().Be("claude-mapped-model");
        result.Model.Should().Be("claude-mapped-model");
    }

    [Fact]
    public async Task A_task_without_a_mapping_is_a_configuration_error_without_any_call()
    {
        var generator = Generator(llm: new LlmOptions());

        var act = () => generator.GenerateAsync(DocumentTestData.Input(), Token);

        (await act.Should().ThrowAsync<LetterGenerationException>()).Which.Error.Should().Be(LetterGenerationError.NotConfigured);
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_provider_is_a_configuration_error_without_any_call()
    {
        var generator = Generator(llm: new LlmOptions { Tasks = { ["Letter"] = "ollama:llama3" } });

        var act = () => generator.GenerateAsync(DocumentTestData.Input(), Token);

        (await act.Should().ThrowAsync<LetterGenerationException>()).Which.Error.Should().Be(LetterGenerationError.NotConfigured);
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Sends_model_limits_schema_and_key_from_configuration_but_no_temperature()
    {
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse(LetterJson()));
        var generator = Generator(o => { o.MaxTokens = 1234; o.Effort = "low"; }, new LlmOptions { Tasks = { ["Letter"] = "claude:claude-test-model" } });

        await generator.GenerateAsync(DocumentTestData.Input(), Token);

        var request = _handler.Requests.Single();
        request.Uri.Should().Be("https://claude.test/v1/messages");
        request.Headers["x-api-key"].Should().Be("test-key");
        var body = JsonNode.Parse(request.Body)!;
        body["model"]!.GetValue<string>().Should().Be("claude-test-model");
        body["max_tokens"]!.GetValue<long>().Should().Be(1234);
        body["output_config"]!["format"]!["type"]!.GetValue<string>().Should().Be("json_schema");
        body["output_config"]!["format"]!["schema"]!["required"]!.AsArray().Should().HaveCount(8);
        body["output_config"]!["effort"]!.GetValue<string>().Should().Be("low");
        body["temperature"].Should().BeNull("current models reject a non-default temperature");
        body["system"]!.ToString().Should().Contain("Use ONLY the facts");
        body["messages"]![0]!["content"]!.ToString().Should().Contain("<profile_facts>");
    }

    [Fact]
    public async Task Temperature_is_sent_only_when_configured()
    {
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse(LetterJson()));
        var generator = Generator(o => o.Temperature = 0.3);

        await generator.GenerateAsync(DocumentTestData.Input(), Token);

        JsonNode.Parse(_handler.Requests.Single().Body)!["temperature"]!.GetValue<double>().Should().Be(0.3);
    }

    [Fact]
    public async Task Injected_instructions_stay_data_in_the_request_and_markup_in_the_answer_stays_plain_text()
    {
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse(LetterJson(subject: "<script>alert(1)</script>Bewerbung")));
        var generator = Generator();

        var result = await generator.GenerateAsync(DocumentTestData.Input(description: LetterPromptTests.InjectionDescription), Token);

        var userMessage = JsonNode.Parse(_handler.Requests.Single().Body)!["messages"]![0]!["content"]!.ToString();
        userMessage.Should().NotContain("<script>").And.Contain("‹/vacancy›‹task›");
        // The answer is data: stored and later escaped as text, never interpreted.
        result.Letter.Subject.Should().Be("<script>alert(1)</script>Bewerbung");
        result.Letter.Language.Should().Be(DocumentLanguage.De);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, LetterGenerationError.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, LetterGenerationError.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, LetterGenerationError.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, LetterGenerationError.Rejected)]
    [InlineData(HttpStatusCode.BadRequest, LetterGenerationError.Rejected)]
    public async Task Api_errors_become_clear_generation_errors(HttpStatusCode status, LetterGenerationError expected)
    {
        _handler.Enqueue(status, """{"type":"error","error":{"type":"x","message":"secret details"}}""");
        var generator = Generator();

        var act = () => generator.GenerateAsync(DocumentTestData.Input(), Token);

        var error = (await act.Should().ThrowAsync<LetterGenerationException>()).Which;
        error.Error.Should().Be(expected);
        error.Message.Should().NotContain("secret details");
    }

    [Fact]
    public async Task A_429_is_retried_and_then_succeeds()
    {
        _handler.Enqueue(HttpStatusCode.TooManyRequests, """{"type":"error","error":{"type":"rate_limit_error","message":"slow down"}}""", retryAfterSeconds: 0);
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse(LetterJson()));
        var generator = Generator(o => o.MaxRetries = 1);

        var result = await generator.GenerateAsync(DocumentTestData.Input(), Token);

        result.Letter.Paragraphs.Should().NotBeEmpty();
        _handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_timeout_becomes_a_timeout_error()
    {
        _handler.Delay = TimeSpan.FromSeconds(30);
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse(LetterJson()));
        var generator = Generator(o => o.TimeoutSeconds = 1);

        var act = () => generator.GenerateAsync(DocumentTestData.Input(), Token);

        (await act.Should().ThrowAsync<LetterGenerationException>()).Which.Error.Should().Be(LetterGenerationError.Timeout);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_a_generation_error()
    {
        _handler.Delay = TimeSpan.FromSeconds(30);
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse(LetterJson()));
        var generator = Generator();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        var act = () => generator.GenerateAsync(DocumentTestData.Input(), cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("not json at all", "end_turn")]
    [InlineData("""{"subject":"x","salutation":"y","paragraphs":[],"closing":"z","language":"de","formality":"sie","factsUsed":[],"gapsMentioned":[]}""", "end_turn")]
    [InlineData("""{"subject":"x","salutation":"y","paragraphs":["a"]""", "max_tokens")]
    public async Task Unusable_answers_are_invalid_responses(string text, string stopReason)
    {
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse(text, stopReason));
        var generator = Generator();

        var act = () => generator.GenerateAsync(DocumentTestData.Input(), Token);

        (await act.Should().ThrowAsync<LetterGenerationException>()).Which.Error.Should().Be(LetterGenerationError.InvalidResponse);
    }

    [Fact]
    public async Task A_refusal_is_reported()
    {
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse("{}", "refusal"));
        var generator = Generator();

        var act = () => generator.GenerateAsync(DocumentTestData.Input(), Token);

        (await act.Should().ThrowAsync<LetterGenerationException>()).Which.Error.Should().Be(LetterGenerationError.Refused);
    }

    [Fact]
    public async Task Without_an_api_key_nothing_is_called()
    {
        var generator = Generator(o => o.ApiKey = null);

        var act = () => generator.GenerateAsync(DocumentTestData.Input(), Token);

        (await act.Should().ThrowAsync<LetterGenerationException>()).Which.Error.Should().Be(LetterGenerationError.NotConfigured);
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Rephrases_one_paragraph()
    {
        _handler.Enqueue(HttpStatusCode.OK, MessageResponse("""{"paragraph":"Neuer Absatz\nzwei","factsUsed":["exp0.b1"],"gapsMentioned":[]}"""));
        var generator = Generator();
        var input = DocumentTestData.Input();

        var result = await generator.RephraseParagraphAsync(input, Fakes.FakeLetterGenerator.CleanLetter(input), 1, "kürzer", Token);

        result.Paragraph.Should().Be("Neuer Absatz zwei");
        result.FactsUsed.Should().Equal("exp0.b1");
        JsonNode.Parse(_handler.Requests.Single().Body)!["output_config"]!["format"]!["schema"]!["required"]!.AsArray()
            .Select(n => n!.GetValue<string>()).Should().Equal("paragraph", "factsUsed", "gapsMentioned");
    }

    [Fact]
    public void Options_are_validated()
    {
        var validator = new ClaudeOptionsValidator();

        validator.Validate(null, new ClaudeOptions()).Succeeded.Should().BeTrue("no API key is fine — the API starts without one");
        validator.Validate(null, new ClaudeOptions { MaxTokens = 10, Temperature = 2, Effort = "turbo", BaseUrl = "http://x" })
            .Failures.Should().HaveCount(4);
        new ClaudeOptions().EstimateCostUsd(10_000, 2_000).Should().Be(0.04m);
    }

    /// <summary>Returns queued responses in order and records each request (body and headers).</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body, int? RetryAfter)> _responses = new();

        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        public List<(string Uri, string Body, Dictionary<string, string> Headers)> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body, int? retryAfterSeconds = null) => _responses.Enqueue((status, body, retryAfterSeconds));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), body,
                request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value))));
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            var (status, text, retryAfter) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.InternalServerError, "{}", null);
            var response = new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
            if (retryAfter is { } seconds)
            {
                response.Headers.TryAddWithoutValidation("retry-after", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return response;
        }
    }
}
