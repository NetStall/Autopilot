using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using JobAutopilot.Api.Features.VacancyImports;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using static JobAutopilot.Tests.Fakes.FakeVacancySource;

namespace JobAutopilot.Tests.Integration;

/// <summary>POST /api/imports (background run) with a fake source: request handling, results, failures.</summary>
public class ImportEndpointsTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private readonly FakeVacancySource _source = new();

    private int ConfiguredSearchCount =>
        Factory.Services.GetRequiredService<IOptions<ImportOptions>>().Value.Searches.Count;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IVacancySource>();
        services.AddSingleton<IVacancySource>(_source);
    }

    [Fact]
    public async Task First_import_adds_and_second_import_updates_only_what_changed()
    {
        _source.Returns(".NET Entwickler", Candidate("10000-1-S"), Candidate("10000-2-S"));
        const string search = """{ "keyword": ".NET Entwickler", "location": "Deggendorf", "radiusKm": 50, "publishedWithinDays": 14 }""";

        var first = await RunImportAsync(search);
        await MarkAllSeenAsync();
        _source.Returns(".NET Entwickler", Candidate("10000-1-S", title: "Geänderter Titel"), Candidate("10000-2-S"));
        var second = await RunImportAsync(search);

        first.Should().BeEquivalentTo(new { Status = ImportRunStatus.Succeeded, Fetched = 2, Added = 2, Updated = 0, Unchanged = 0 });
        second.Should().BeEquivalentTo(new { Status = ImportRunStatus.Succeeded, Fetched = 2, Added = 0, Updated = 1, Unchanged = 1 });

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
        var run = await RunImportAsync();

        run.Status.Should().Be(ImportRunStatus.Succeeded);
        run.Trigger.Should().Be(ImportTrigger.Manual);
        run.SearchesTotal.Should().Be(ConfiguredSearchCount);
        run.SearchesDone.Should().Be(ConfiguredSearchCount);
        run.Searches.Should().HaveCount(ConfiguredSearchCount);
        run.DurationMs.Should().NotBeNull();
        // The fake stands in for the Arbeitsagentur; the Himalayas searches find no client and are skipped with an error.
        var arbeitsagenturSearches = Factory.Services.GetRequiredService<IOptions<ImportOptions>>().Value.Searches
            .Count(s => s.Source == VacancySource.Arbeitsagentur);
        _source.Searches.Should().HaveCount(arbeitsagenturSearches);
        run.Searches.Where(s => s.Source == VacancySource.Himalayas).Should().OnlyContain(s => s.Error != null);
        _source.Searches[0].Should().Be(new VacancySearch
        {
            Name = "region-softwareentwickler", Keyword = "Softwareentwickler", Location = "Deggendorf", RadiusKm = 70,
        });
    }

    [Fact]
    public async Task Empty_form_post_like_windows_powershell_runs_all_configured_searches()
    {
        // Invoke-RestMethod -Method Post (Windows PowerShell 5.1) sends this content type with an empty body.
        using var content = new StringContent(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await Client.PostAsync("/api/imports", content, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var started = (await response.Content.ReadFromJsonAsync<ImportStartedDto>(ApiJson, CancellationToken))!;
        (await WaitForImportAsync(started.ImportId)).SearchesTotal.Should().Be(ConfiguredSearchCount);
    }

    [Fact]
    public async Task Non_json_body_returns_415()
    {
        using var content = new StringContent("keyword=Dev", Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await Client.PostAsync("/api/imports", content, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        _source.Searches.Should().BeEmpty();
    }

    [Fact]
    public async Task Malformed_json_returns_400()
    {
        var response = await PostImportAsync("{ keyword: ");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _source.Searches.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "keyword": "" }""", "Keyword")]
    [InlineData("""{ "keyword": "Dev", "location": "Passau", "radiusKm": 201 }""", "RadiusKm")]
    [InlineData("""{ "keyword": "Dev", "location": "Passau", "radiusKm": -1 }""", "RadiusKm")]
    [InlineData("""{ "keyword": "Dev", "publishedWithinDays": 101 }""", "PublishedWithinDays")]
    public async Task Invalid_search_returns_validation_problem(string body, string expectedField)
    {
        var response = await PostImportAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken);
        problem!.Errors.Keys.Should().Equal(expectedField);
        _source.Searches.Should().BeEmpty();
    }

    [Fact]
    public async Task Unreachable_source_fails_the_run_and_saves_nothing()
    {
        _source.Returns(".NET Entwickler", Candidate("10000-1-S"));
        _source.IsUnavailable = true;

        var run = await RunImportAsync();

        run.Status.Should().Be(ImportRunStatus.Failed);
        run.Error.Should().StartWith("No job source was reachable. Nothing was imported.");
        run.FinishedAt.Should().NotBeNull();
        await using var db = Postgres.CreateDbContext();
        (await db.Vacancies.CountAsync(CancellationToken)).Should().Be(0);
        (await db.ImportRuns.SingleAsync(CancellationToken)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_import_id_is_404()
    {
        var response = await Client.GetAsync($"/api/imports/{Guid.NewGuid()}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Recent_imports_are_listed_newest_first_with_a_limit()
    {
        var first = await RunImportAsync("""{ "keyword": "A" }""");
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await RunImportAsync("""{ "keyword": "B" }""");

        var all = await Client.GetFromJsonAsync<List<ImportRunDto>>("/api/imports", ApiJson, CancellationToken);
        var one = await Client.GetFromJsonAsync<List<ImportRunDto>>("/api/imports?limit=1", ApiJson, CancellationToken);

        all!.Select(r => r.Id).Should().Equal(second.Id, first.Id);
        one!.Select(r => r.Id).Should().Equal(second.Id);
        (await Client.GetAsync("/api/imports?limit=0", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.GetAsync("/api/imports?limit=51", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task MarkAllSeenAsync()
    {
        await using var db = Postgres.CreateDbContext();
        await db.Vacancies.ExecuteUpdateAsync(s => s.SetProperty(v => v.IsSeen, true), CancellationToken);
    }
}
