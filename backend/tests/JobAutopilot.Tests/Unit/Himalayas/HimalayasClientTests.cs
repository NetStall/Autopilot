using System.Net;
using System.Text;
using FluentAssertions;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure;
using JobAutopilot.Infrastructure.Himalayas;
using JobAutopilot.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobAutopilot.Tests.Unit.Himalayas;

/// <summary>The Himalayas client with the real DI registration (resilience included) and a fake handler — no network.</summary>
public sealed class HimalayasClientTests : IDisposable
{
    private static readonly VacancySearch Search = new()
    {
        Name = "himalayas-dotnet", Keyword = ".NET", Source = VacancySource.Himalayas, RemoteOnly = true,
    };

    // 2026-10-19 12:00 UTC; page 1's first job is from 2026-10-18, the third from June 2026.
    private readonly TestTimeProvider _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeHandler _handler = new();
    private ServiceProvider? _services;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pages_until_the_total_is_reached_and_sends_the_documented_query()
    {
        _handler.Responses.Enqueue(Ok("page1.json"));
        _handler.Responses.Enqueue(Ok("page2.json"));

        var candidates = await SearchAsync(Search);

        _handler.Requests.Should().Equal(
            "https://himalayas.app/jobs/api/search?q=.NET&country=DE&exclude_worldwide=true&sort=recent&page=1",
            "https://himalayas.app/jobs/api/search?q=.NET&country=DE&exclude_worldwide=true&sort=recent&page=2");
        candidates.Select(c => c.ExternalId).Should().Equal("senior-net-developer-4267385201", "c-entwickler-3");
    }

    [Fact]
    public async Task Maps_a_job_with_application_link_plain_text_and_full_remote()
    {
        _handler.Responses.Enqueue(Ok("page1.json"));
        _handler.Responses.Enqueue(Ok("page2.json"));

        var job = (await SearchAsync(Search))[0];

        job.Title.Should().Be("Senior .NET Developer (m/w/d) - remote");
        job.Company.Should().Be("Beispiel Software GmbH");
        job.Location.Should().Be("Remote (Deutschland)");
        job.Url.Should().StartWith("https://himalayas.app/companies/beispiel-software/jobs/senior-net-developer-4267385201?utm_source=api",
            "the terms require the API's applicationLink for applying");
        job.RemoteHint.Should().Be(RemoteLevel.FullRemote);
        job.PublishedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1792386898));
        job.ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1797570898));
        job.Description.Should().Be("Über uns\n\nWir entwickeln mit C#, ASP.NET Core & Vue.js.\n\n- 100 % remote\n- PostgreSQL");
    }

    [Fact]
    public async Task Skips_jobs_not_open_to_germany_and_older_than_the_search_asks_for()
    {
        _handler.Responses.Enqueue(Ok("page1.json"));
        _handler.Responses.Enqueue(Ok("page2.json"));

        var candidates = await SearchAsync(Search);

        candidates.Should().NotContain(c => c.ExternalId == "backend-engineer-1", "US only");
        candidates.Should().NotContain(c => c.ExternalId == "fullstack-old-2", "published in June, the search asks for 30 days");
        candidates[1].PublishedAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1792300000000), "milliseconds are understood too");
        candidates[1].Location.Should().Be("Remote (2 Länder inkl. Deutschland)");
    }

    [Fact]
    public async Task Respects_the_page_limit()
    {
        _handler.Responses.Enqueue(Ok("page1.json"));

        await SearchAsync(Search with { MaxPages = 1 });

        _handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Rate_limiting_is_retried()
    {
        _handler.Responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero) },
        });
        _handler.Responses.Enqueue(Ok("page1.json"));
        _handler.Responses.Enqueue(Ok("page2.json"));

        var candidates = await SearchAsync(Search);

        candidates.Should().HaveCount(2);
        _handler.Requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}")]
    [InlineData(HttpStatusCode.OK, "<html>maintenance</html>")]
    public async Task Unusable_responses_become_source_unavailable(HttpStatusCode status, string body)
    {
        _handler.Responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });

        var act = () => SearchAsync(Search);

        await act.Should().ThrowAsync<VacancySourceUnavailableException>();
    }

    [Fact]
    public async Task An_api_key_is_sent_as_bearer_token_when_configured()
    {
        _handler.Responses.Enqueue(Ok("page2.json"));

        await SearchAsync(Search, apiKey: "test-key");

        _handler.Authorization.Should().Be("Bearer test-key");
    }

    public void Dispose() => _services?.Dispose();

    private async Task<List<VacancyCandidate>> SearchAsync(VacancySearch search, string? apiKey = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=unused",
                ["Himalayas:DelayBetweenPagesMs"] = "0",
                ["Himalayas:ApiKey"] = apiKey,
            })
            .Build();

        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration);
        services.AddSingleton<TimeProvider>(_clock);
        services.AddHttpClient<HimalayasClient>().ConfigurePrimaryHttpMessageHandler(() => _handler);
        _services = services.BuildServiceProvider();

        var source = _services.GetServices<IVacancySource>().Single(s => s.Source == VacancySource.Himalayas);
        var result = new List<VacancyCandidate>();
        await foreach (var candidate in source.SearchAsync(search, CancellationToken))
        {
            result.Add(candidate);
        }

        return result;
    }

    private static Func<HttpResponseMessage> Ok(string fixture) => () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "himalayas", fixture)),
            Encoding.UTF8,
            "application/json"),
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Queue<Func<HttpResponseMessage>> Responses { get; } = new();

        public List<string> Requests { get; } = [];

        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            Authorization = request.Headers.Authorization?.ToString();
            var response = Responses.Count > 0
                ? Responses.Dequeue()()
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "jobs": [], "totalCount": 0 }""") };
            return Task.FromResult(response);
        }
    }
}
