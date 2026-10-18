using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static JobAutopilot.Tests.Fakes.FakeVacancySource;

namespace JobAutopilot.Tests.Integration;

public class ImportEndpointsTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private const string Url = "/api/imports/arbeitsagentur";

    private readonly FakeVacancySource _source = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IVacancySource>();
        services.AddSingleton<IVacancySource>(_source);
    }

    [Fact]
    public async Task First_import_adds_and_second_import_updates_only_what_changed()
    {
        _source.Returns(".NET Entwickler", Candidate("10000-1-S"), Candidate("10000-2-S"));
        var search = new { keyword = ".NET Entwickler", location = "Deggendorf", radiusKm = 50, publishedWithinDays = 14 };

        var first = await PostAsync(search);
        await MarkAllSeenAsync();
        _source.Returns(".NET Entwickler", Candidate("10000-1-S", title: "Geänderter Titel"), Candidate("10000-2-S"));
        var second = await PostAsync(search);

        first.Should().BeEquivalentTo(new { Fetched = 2, Added = 2, Updated = 0, Unchanged = 0 });
        second.Should().BeEquivalentTo(new { Fetched = 2, Added = 0, Updated = 1, Unchanged = 1 });

        await using var db = Postgres.CreateDbContext();
        var stored = await db.Vacancies.OrderBy(v => v.ExternalId).ToListAsync(CancellationToken);
        stored.Should().HaveCount(2);
        stored[0].Title.Should().Be("Geänderter Titel");
        stored.Should().AllSatisfy(v =>
        {
            v.Source.Should().Be(VacancySource.Arbeitsagentur);
            v.IsSeen.Should().BeTrue();
        });
    }

    [Fact]
    public async Task Empty_body_runs_all_configured_searches()
    {
        var response = await Client.PostAsync(Url, content: null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _source.Searches.Should().HaveCount(14);
        _source.Searches[0].Should().Be(new VacancySearch
        {
            Name = "region-softwareentwickler", Keyword = "Softwareentwickler", Location = "Deggendorf", RadiusKm = 70,
        });
    }

    [Fact]
    public async Task Empty_form_post_like_windows_powershell_runs_all_configured_searches()
    {
        // Invoke-RestMethod -Method Post (Windows PowerShell 5.1) sends this content type with an empty body.
        using var content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await Client.PostAsync(Url, content, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _source.Searches.Should().HaveCount(14);
    }

    [Fact]
    public async Task Non_json_body_returns_415()
    {
        using var content = new StringContent("keyword=Dev", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await Client.PostAsync(Url, content, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        _source.Searches.Should().BeEmpty();
    }

    [Fact]
    public async Task Malformed_json_returns_400()
    {
        using var content = new StringContent("{ keyword: ", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync(Url, content, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _source.Searches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "keyword": "" }""", "Keyword")]
    [InlineData("""{ "keyword": "Dev", "radiusKm": 201 }""", "RadiusKm")]
    [InlineData("""{ "keyword": "Dev", "radiusKm": -1 }""", "RadiusKm")]
    [InlineData("""{ "keyword": "Dev", "publishedWithinDays": 101 }""", "PublishedWithinDays")]
    public async Task Invalid_search_returns_validation_problem(string body, string expectedField)
    {
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync(Url, content, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken);
        problem!.Errors.Keys.Should().Equal(expectedField);
        _source.Searches.Should().BeEmpty();
    }

    [Fact]
    public async Task Unreachable_source_returns_502_and_saves_nothing()
    {
        _source.Returns(".NET Entwickler", Candidate("10000-1-S"));
        _source.IsUnavailable = true;

        var response = await Client.PostAsync(Url, content: null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        await using var db = Postgres.CreateDbContext();
        (await db.Vacancies.CountAsync(CancellationToken)).Should().Be(0);
    }

    private async Task<ImportResultDto> PostAsync(object search)
    {
        var response = await Client.PostAsJsonAsync(Url, search, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ImportResultDto>(CancellationToken))!;
    }

    private async Task MarkAllSeenAsync()
    {
        await using var db = Postgres.CreateDbContext();
        await db.Vacancies.ExecuteUpdateAsync(s => s.SetProperty(v => v.IsSeen, true), CancellationToken);
    }
}
