using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Vacancies;
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
using static JobAutopilot.Tests.Integration.VacancySeed;

namespace JobAutopilot.Tests.Integration;

/// <summary>Remote and search filters, configured searches and the extended import response (task 03b).</summary>
public class BroaderSearchApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly FakeVacancySource _source = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IVacancySource>();
        services.AddSingleton<IVacancySource>(_source);
    }

    [Fact]
    public async Task Remote_filter_returns_only_remote_or_only_other_vacancies()
    {
        await SeedAsync(
            Make(title: "Remote", isRemote: true, matchedSearches: ["remote-dotnet"]),
            Make(title: "Region", isRemote: false, matchedSearches: ["region-dotnet"]));

        (await GetAsync("?remote=true")).Items.Select(v => v.Title).Should().Equal("Remote");
        (await GetAsync("?remote=false")).Items.Select(v => v.Title).Should().Equal("Region");
        (await GetAsync(string.Empty)).TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Search_filter_matches_any_of_the_given_names()
    {
        await SeedAsync(
            Make(title: "A", matchedSearches: ["region-csharp", "remote-csharp"]),
            Make(title: "B", matchedSearches: ["region-vue"]),
            Make(title: "C", matchedSearches: []));

        (await GetAsync("?search=remote-csharp")).Items.Select(v => v.Title).Should().Equal("A");
        (await GetAsync("?search=region-vue&search=region-csharp")).Items.Select(v => v.Title)
            .Should().BeEquivalentTo("A", "B");
        (await GetAsync("?search=unknown")).Items.Should().BeEmpty();
        (await GetAsync("?search=region-csharp&remote=true")).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Vacancy_dto_contains_remote_flag_and_matched_searches()
    {
        await SeedAsync(Make(title: "A", isRemote: true, matchedSearches: ["remote-csharp", "region-csharp"]));

        var dto = (await GetAsync(string.Empty)).Items.Single();

        dto.IsRemote.Should().BeTrue();
        dto.MatchedSearches.Should().Equal("remote-csharp", "region-csharp");
    }

    [Fact]
    public async Task Too_many_or_blank_search_names_are_rejected()
    {
        var tooMany = string.Join('&', Enumerable.Range(0, VacancyListQuery.MaxSearchNames + 1).Select(i => $"search=s{i}"));

        var response = await Client.GetAsync($"/api/vacancies?{tooMany}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.GetAsync("/api/vacancies?search=%20", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Searches_endpoint_lists_the_configured_searches()
    {
        var configured = Factory.Services.GetRequiredService<IOptions<ImportOptions>>().Value.Searches;

        var searches = await Client.GetFromJsonAsync<List<ConfiguredSearchDto>>("/api/imports/searches", CancellationToken);

        searches.Should().HaveCount(configured.Count).And.HaveCountGreaterThan(5);
        searches!.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        searches.Should().Contain(s => s.RemoteOnly && s.Location == null && s.RadiusKm == null);
        searches.Should().Contain(s => !s.RemoteOnly && s.Location == "Deggendorf" && s.RadiusKm == 70);
        searches.Should().OnlyContain(s => s.PublishedWithinDays == 30);
    }

    [Fact]
    public async Task Configured_defaults_are_valid_and_exclude_typical_noise()
    {
        var options = Factory.Services.GetRequiredService<IOptions<ImportOptions>>().Value;

        VacancySearchRules.Validate(options.Searches).Should().BeEmpty();
        options.ExcludeTitleTerms.Should().Contain(["Ausbildung", "Praktikum", "Werkstudent", "Duales Studium"]);
    }

    [Fact]
    public async Task Import_response_has_excluded_and_a_per_search_breakdown_and_stores_remote_flags()
    {
        _source
            .Returns("remote-test", Candidate("R-1"), Candidate("B-1"))
            .Returns("region-test", Candidate("B-1"), Candidate("A-1", "Ausbildung Fachinformatiker"));

        var remote = await PostSearchAsync("""{ "name": "remote-test", "keyword": "C#", "remoteOnly": true }""");
        var region = await PostSearchAsync(
            """{ "name": "region-test", "keyword": "C#", "location": "Deggendorf", "radiusKm": 70 }""");

        remote.Should().BeEquivalentTo(new { Fetched = 2, Added = 2, Excluded = 0 });
        region.Should().BeEquivalentTo(new { Fetched = 2, Added = 0, Updated = 1, Unchanged = 0, Excluded = 1 });
        region.Searches.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Name = "region-test", Keyword = "C#", Location = "Deggendorf", RemoteOnly = false,
            Fetched = 2, Added = 0, Updated = 1, Excluded = 1,
        });

        await using var db = Postgres.CreateDbContext();
        var stored = await db.Vacancies.OrderBy(v => v.ExternalId).ToListAsync(CancellationToken);
        stored.Select(v => v.ExternalId).Should().Equal("B-1", "R-1");
        stored[0].IsRemote.Should().BeTrue("found by the remote search first; the region search must not reset it");
        stored[0].MatchedSearches.Should().Equal("remote-test", "region-test");
        stored[1].MatchedSearches.Should().Equal("remote-test");
    }

    [Fact]
    public async Task Ad_hoc_search_defaults_its_name_and_published_days()
    {
        await PostSearchAsync("""{ "keyword": "Vue" }""");

        _source.Searches.Single().Should().Be(new VacancySearch { Name = "ad-hoc", Keyword = "Vue", PublishedWithinDays = 30 });
    }

    [Theory]
    [InlineData("""{ "keyword": "C#", "radiusKm": 50 }""", "RadiusKm")]
    [InlineData("""{ "keyword": "C#", "name": "with space" }""", "Name")]
    [InlineData("""{ "keyword": "C#", "maxPages": 0 }""", "MaxPages")]
    public async Task Invalid_ad_hoc_search_is_a_validation_problem(string body, string field)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/imports/arbeitsagentur", content, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken);
        problem!.Errors.Keys.Should().Equal(field);
        _source.Searches.Should().BeEmpty();
    }

    private async Task<ImportResultDto> PostSearchAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/imports/arbeitsagentur", content, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ImportResultDto>(CancellationToken))!;
    }

    private async Task SeedAsync(params Vacancy[] vacancies)
    {
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.AddRange(vacancies);
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<PagedResponse<VacancyDto>> GetAsync(string query) =>
        (await Client.GetFromJsonAsync<PagedResponse<VacancyDto>>($"/api/vacancies{query}", JsonOptions, CancellationToken))!;
}
