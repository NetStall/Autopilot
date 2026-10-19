using System.Net;
using System.Text;
using FluentAssertions;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure;
using JobAutopilot.Infrastructure.Arbeitsagentur;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobAutopilot.Tests.Unit.Arbeitsagentur;

/// <summary>Details endpoint with the real DI registration and a fake handler (no network).</summary>
public sealed class ArbeitsagenturDetailsTests : IDisposable
{
    private readonly FakeHandler _handler = new();
    private ServiceProvider? _services;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requests_the_base64_encoded_reference_number_and_maps_the_details()
    {
        _handler.Respond(HttpStatusCode.OK, Fixture("details-temporary.json"));

        var details = await Source().GetDetailsAsync("12265-349789_JB5275760-S", CancellationToken);

        _handler.Requests.Should().ContainSingle().Which.Should().Be(
            "https://rest.arbeitsagentur.de/jobboerse/jobsuche-service/pc/v4/jobdetails/MTIyNjUtMzQ5Nzg5X0pCNTI3NTc2MC1T");
        details.Should().NotBeNull();
        details!.EmployerHint.Should().Be(EmployerType.Temporary, "istArbeitnehmerUeberlassung = true");
        details.Description.Should().StartWith("Für unseren Kunden suchen wir dich")
            .And.Contain("\n- Entwicklung mit .NET")
            .And.NotContain("\r")
            .And.EndWith("Homeoffice nach Absprache möglich.");
        details.RemoteHint.Should().Be(RemoteLevel.Unknown, "no home-office fields in this posting");
    }

    [Fact]
    public async Task Padding_is_url_encoded_and_home_office_flags_are_mapped()
    {
        _handler.Respond(HttpStatusCode.OK, Fixture("details-direct.json"));

        var details = await Source().GetDetailsAsync("14089-01499270-S", CancellationToken);

        _handler.Requests.Single().Should().EndWith("/pc/v4/jobdetails/MTQwODktMDE0OTkyNzAtUw%3D%3D");
        details!.RemoteHint.Should().Be(RemoteLevel.FullRemote);
        details.EmployerHint.Should().Be(EmployerType.Direct);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task A_posting_that_is_gone_returns_null(HttpStatusCode status)
    {
        _handler.Respond(status, "{}");

        (await Source().GetDetailsAsync("10000-1-S", CancellationToken)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Access Denied")]
    [InlineData(HttpStatusCode.OK, "<html>no json</html>")]
    public async Task Unusable_responses_become_source_unavailable(HttpStatusCode status, string body)
    {
        _handler.Respond(status, body);

        var act = () => Source().GetDetailsAsync("10000-1-S", CancellationToken);

        await act.Should().ThrowAsync<VacancySourceUnavailableException>();
    }

    [Theory]
    [InlineData(true, null, RemoteLevel.Hybrid)]
    [InlineData(true, 40.0, RemoteLevel.Hybrid)]
    [InlineData(true, 100.0, RemoteLevel.FullRemote)]
    [InlineData(null, 100.0, RemoteLevel.FullRemote)]
    [InlineData(false, null, RemoteLevel.Unknown)]
    [InlineData(null, null, RemoteLevel.Unknown)]
    public void Home_office_fields_map_to_a_remote_hint(bool? possible, double? percent, RemoteLevel expected) =>
        ArbeitsagenturMapper.MapRemote(possible, percent).Should().Be(expected);

    [Fact]
    public void Change_date_is_german_local_time()
    {
        ArbeitsagenturMapper.ParseChangedAt("2026-10-13T09:04:40.708")
            .Should().Be(new DateTimeOffset(2026, 10, 13, 9, 4, 40, 708, TimeSpan.FromHours(2)));
        ArbeitsagenturMapper.ParseChangedAt("2026-12-01T09:00:00")
            .Should().Be(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.FromHours(1)));
        ArbeitsagenturMapper.ParseChangedAt("kaputt").Should().BeNull();
    }

    [Fact]
    public void Search_results_carry_remote_hint_and_change_date()
    {
        var item = new JobSearchItem(
            "10000-1-S", "Entwickler", "Firma", null, null, null, null,
            Aenderungsdatum: "2026-10-13T09:04:40.708", Homeofficemoeglich: true, Homeofficeprozent: null);

        var candidate = ArbeitsagenturMapper.ToCandidate(item)!;

        candidate.RemoteHint.Should().Be(RemoteLevel.Hybrid);
        candidate.ChangedAt.Should().NotBeNull();
        candidate.Description.Should().BeNull("the search returns no description");
    }

    public void Dispose() => _services?.Dispose();

    private IVacancyDetailsSource Source()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=unused" })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(configuration);
        services.AddHttpClient<ArbeitsagenturClient>().ConfigurePrimaryHttpMessageHandler(() => _handler);
        _services = services.BuildServiceProvider();
        return _services.GetRequiredService<IVacancyDetailsSource>();
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arbeitsagentur", name));

    private sealed class FakeHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "{}";

        public List<string> Requests { get; } = [];

        public void Respond(HttpStatusCode status, string body) => (_status, _body) = (status, body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
