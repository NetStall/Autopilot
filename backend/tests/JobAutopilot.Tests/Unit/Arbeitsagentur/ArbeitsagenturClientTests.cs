using System.Net;
using System.Text;
using FluentAssertions;
using JobAutopilot.Core.Import;
using JobAutopilot.Infrastructure;
using JobAutopilot.Infrastructure.Arbeitsagentur;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobAutopilot.Tests.Unit.Arbeitsagentur;

/// <summary>
/// Uses the real DI registration from <see cref="DependencyInjection.AddInfrastructure"/> (headers, base address,
/// resilience pipeline) with a fake handler at the bottom, so no request ever leaves the process.
/// </summary>
public sealed class ArbeitsagenturClientTests : IDisposable
{
    private static readonly VacancySearch Search = new()
    {
        Name = "region-dotnet", Keyword = ".NET Entwickler", Location = "Deggendorf", RadiusKm = 50, PublishedWithinDays = 14,
    };

    private readonly FakeJobsucheHandler _handler = new();
    private ServiceProvider? _services;

    [Fact]
    public async Task Sends_the_documented_url_query_and_headers()
    {
        _handler.Pages[1] = "empty.json";

        await SearchAsync(Search);

        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Uri.Should().Be(
            "https://rest.arbeitsagentur.de/jobboerse/jobsuche-service/pc/v6/jobs" +
            "?was=.NET%20Entwickler&angebotsart=1&wo=Deggendorf&umkreis=50&veroeffentlichtseit=14&page=1&size=2");
        request.Headers["X-API-Key"].Should().Be("jobboerse-jobsuche");
        request.Headers["User-Agent"].Should().StartWith("job-autopilot/");
    }

    [Fact]
    public async Task Optional_parameters_are_left_out()
    {
        _handler.Pages[1] = "empty.json";

        await SearchAsync(new VacancySearch { Name = "remote-csharp", Keyword = "C#", PublishedWithinDays = 30 });

        _handler.Requests.Single().Uri.Should().EndWith("pc/v6/jobs?was=C%23&angebotsart=1&veroeffentlichtseit=30&page=1&size=2");
    }

    [Fact]
    public async Task Remote_only_without_location_searches_all_of_germany_with_the_home_office_filter()
    {
        _handler.Pages[1] = "empty.json";

        await SearchAsync(new VacancySearch { Name = "remote-dotnet", Keyword = ".NET", RemoteOnly = true });

        _handler.Requests.Single().Uri.Should().EndWith(
            "pc/v6/jobs?was=.NET&angebotsart=1&homeoffice=prozentual_0%3Bnv_true&veroeffentlichtseit=30&page=1&size=2");
    }

    [Fact]
    public async Task Remote_filter_is_configurable_and_url_encoded()
    {
        _handler.Pages[1] = "empty.json";

        await SearchAsync(
            new VacancySearch { Name = "remote-csharp", Keyword = "C#", RemoteOnly = true },
            remoteFilter: "prozentual_100");

        var uri = _handler.Requests.Single().Uri;
        uri.Should().Contain("was=C%23&");
        uri.Should().Contain("&homeoffice=prozentual_100&");
        uri.Should().NotContain("wo=").And.NotContain("umkreis=");
    }

    [Theory]
    [InlineData("C#", "was=C%23&")]
    [InlineData(".NET", "was=.NET&")]
    [InlineData("C# .NET", "was=C%23%20.NET&")]
    [InlineData("Full Stack", "was=Full%20Stack&")]
    [InlineData("Softwareentwickler C++", "was=Softwareentwickler%20C%2B%2B&")]
    public void Keywords_are_url_encoded(string keyword, string expected)
    {
        var uri = ArbeitsagenturClient.BuildSearchUri(
            new VacancySearch { Name = "x", Keyword = keyword }, page: 1, pageSize: 100, "prozentual_100");

        uri.Should().Contain(expected);
        uri.Should().NotContain("#", "an unencoded # would start the URL fragment and cut the keyword");
    }

    [Fact]
    public async Task Per_search_max_pages_overrides_the_global_limit()
    {
        _handler.Pages[1] = "page1.json";
        _handler.Pages[2] = "page2.json";
        _handler.Pages[3] = "page3.json";

        await SearchAsync(Search with { MaxPages = 1 }, maxPages: 10);

        _handler.Requests.Select(r => r.Page).Should().Equal(1);
    }

    [Fact]
    public async Task Pages_until_max_ergebnisse_is_reached_and_skips_unusable_entries()
    {
        _handler.Pages[1] = "page1.json";
        _handler.Pages[2] = "page2.json";
        _handler.Pages[3] = "page3.json";

        var candidates = await SearchAsync(Search, maxPages: 10);

        // maxErgebnisse = 5, size = 2 → pages 1..3; page 2 has one entry without referenznummer.
        _handler.Requests.Select(r => r.Page).Should().Equal(1, 2, 3);
        candidates.Select(c => c.ExternalId).Should().Equal(
            "10000-0000000001-S", "10000-0000000002-S", "10000-0000000003-S", "10000-0000000004-S");
    }

    [Fact]
    public async Task Stops_at_max_pages()
    {
        _handler.Pages[1] = "page1.json";
        _handler.Pages[2] = "page2.json";
        _handler.Pages[3] = "page3.json";

        var candidates = await SearchAsync(Search, maxPages: 2);

        _handler.Requests.Select(r => r.Page).Should().Equal(1, 2);
        candidates.Should().HaveCount(3);
    }

    [Fact]
    public async Task Stops_at_an_empty_page()
    {
        _handler.Pages[1] = "page1.json";
        _handler.Pages[2] = "empty.json";
        _handler.Pages[3] = "page3.json";

        var candidates = await SearchAsync(Search, maxPages: 10);

        _handler.Requests.Select(r => r.Page).Should().Equal(1, 2);
        candidates.Should().HaveCount(2);
    }

    [Fact]
    public async Task Maps_the_fixture_fields()
    {
        _handler.Pages[1] = "page1.json";
        _handler.Pages[2] = "empty.json";

        var candidates = await SearchAsync(Search);

        candidates[0].Location.Should().Be("94469 Deggendorf");
        candidates[0].Url.Should().Be("https://www.arbeitsagentur.de/jobsuche/jobdetail/10000-0000000001-S");
        candidates[1].Location.Should().Be("Regensburg");
        candidates[1].Url.Should().Be("https://karriere.example.de/jobs/42?cp=BA");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Access Denied")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>")]
    public async Task Unusable_responses_become_source_unavailable(HttpStatusCode status, string body)
    {
        _handler.Override = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) };

        var act = () => SearchAsync(Search);

        await act.Should().ThrowAsync<VacancySourceUnavailableException>();
    }

    public void Dispose() => _services?.Dispose();

    private async Task<List<VacancyCandidate>> SearchAsync(
        VacancySearch search,
        int maxPages = 5,
        string remoteFilter = "prozentual_0;nv_true")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=unused",
                ["Arbeitsagentur:PageSize"] = "2",
                ["Arbeitsagentur:MaxPages"] = maxPages.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Arbeitsagentur:DelayBetweenPagesMs"] = "0",
                ["Arbeitsagentur:RemoteHomeOfficeFilter"] = remoteFilter,
            })
            .Build();

        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration);
        services.AddHttpClient<ArbeitsagenturClient>().ConfigurePrimaryHttpMessageHandler(() => _handler);
        _services = services.BuildServiceProvider();

        var source = _services.GetServices<IVacancySource>().Single(s => s.Source == Core.Vacancies.VacancySource.Arbeitsagentur);
        var result = new List<VacancyCandidate>();
        await foreach (var candidate in source.SearchAsync(search, TestContext.Current.CancellationToken))
        {
            result.Add(candidate);
        }

        return result;
    }

    /// <summary>Serves fixture files by the <c>page</c> query parameter and records every request.</summary>
    private sealed class FakeJobsucheHandler : HttpMessageHandler
    {
        public Dictionary<int, string> Pages { get; } = [];

        public HttpResponseMessage? Override { get; set; }

        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var page = int.Parse(
                System.Web.HttpUtility.ParseQueryString(uri.Query)["page"]!,
                System.Globalization.CultureInfo.InvariantCulture);
            Requests.Add(new RecordedRequest(
                uri.AbsoluteUri,
                page,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(' ', h.Value), StringComparer.OrdinalIgnoreCase)));

            if (Override is not null)
            {
                return Task.FromResult(Override);
            }

            var file = Pages.GetValueOrDefault(page) ?? "empty.json";
            var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arbeitsagentur", file));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed record RecordedRequest(string Uri, int Page, Dictionary<string, string> Headers);
}
