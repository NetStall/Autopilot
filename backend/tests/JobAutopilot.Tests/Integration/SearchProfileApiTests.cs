using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.Scoring;
using JobAutopilot.Api.Features.SearchProfiles;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Import;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.SearchProfiles;
using JobAutopilot.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static JobAutopilot.Tests.Integration.VacancySeed;

namespace JobAutopilot.Tests.Integration;

/// <summary>The Suchprofil through the API and its effect on the list and the import.</summary>
public class SearchProfileApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private readonly FakeVacancySource _source = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IVacancySource>();
        services.AddSingleton<IVacancySource>(_source);
    }

    [Fact]
    public async Task It_is_seeded_from_the_configuration_on_first_start()
    {
        var dto = await GetAsync();

        dto.Version.Should().Be(1);
        dto.Profile.SearchTerms.Should().Contain(t => t.Text == "Softwareentwickler");
        dto.Profile.Places.Should().ContainSingle(p => p.City == "Deggendorf" && p.RadiusKm == 70);
        dto.Searches.Should().Contain(s => s.Name == "region-softwareentwickler");
        (await HistoryAsync()).Should().ContainSingle().Which.Summary.Should().StartWith("Angelegt");
    }

    [Fact]
    public async Task Saving_validates_bumps_the_version_records_history_and_triggers_rescoring()
    {
        var dto = await GetAsync();
        dto.Profile.MinScore = 60;
        dto.Profile.SearchTerms.Add(new SearchTerm { Text = "Blazor", Scope = SearchTermScope.Remote });

        var response = await Client.PutAsJsonAsync("/api/search-profile", new SaveSearchProfileRequest(dto.Version, dto.Profile), ApiJson, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = (await response.Content.ReadFromJsonAsync<SaveSearchProfileResponse>(ApiJson, CancellationToken))!;
        saved.SearchProfile.Version.Should().Be(2);
        saved.SearchProfile.Searches.Should().Contain(s => s.Name == "remote-blazor").And.Contain(s => s.Name == "himalayas-blazor");
        saved.Rescore.State.Should().NotBe(RescoreState.Idle, "re-scoring was queued");
        (await HistoryAsync()).First().Summary.Should().Contain("+Blazor (Remote)").And.Contain("Mindestbewertung 0 → 60");

        var invalid = dto.Profile;
        invalid.TopN = 0;
        var bad = await Client.PutAsJsonAsync("/api/search-profile", new SaveSearchProfileRequest(2, invalid), ApiJson, CancellationToken);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("Top N: 1 bis 200.");
    }

    [Fact]
    public async Task A_save_from_a_stale_version_is_a_conflict()
    {
        var dto = await GetAsync();
        (await Client.PutAsJsonAsync("/api/search-profile", new SaveSearchProfileRequest(dto.Version, dto.Profile), ApiJson, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await Client.PutAsJsonAsync("/api/search-profile", new SaveSearchProfileRequest(dto.Version, dto.Profile), ApiJson, CancellationToken);

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("inzwischen geändert");
    }

    [Fact]
    public async Task Rollback_saves_an_earlier_version_as_a_new_one()
    {
        var dto = await GetAsync();
        var originalTerms = dto.Profile.SearchTerms.Count;
        dto.Profile.SearchTerms.Add(new SearchTerm { Text = "Kotlin" });
        await Client.PutAsJsonAsync("/api/search-profile", new SaveSearchProfileRequest(1, dto.Profile), ApiJson, CancellationToken);

        var response = await Client.PostAsJsonAsync("/api/search-profile/rollback", new RollbackRequest(1, 2), ApiJson, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await GetAsync();
        current.Version.Should().Be(3);
        current.Profile.SearchTerms.Should().HaveCount(originalTerms);
        (await HistoryAsync()).First().Summary.Should().StartWith("Zurück auf Version 1: Suchbegriffe: −Kotlin");
    }

    [Fact]
    public async Task Preview_scores_without_saving_anything()
    {
        await SeedAsync(
            Scored(Make(title: "Softwareentwickler C# (m/w/d)", location: "94469 Deggendorf"), 80),
            Scored(Make(title: "Java Developer (m/w/d)"), 10));
        var dto = await GetAsync();
        dto.Profile.Exclude.RemoveAll(k => k.Name == "Java");

        var preview = await Client.PostAsJsonAsync("/api/search-profile/preview", dto.Profile, ApiJson, CancellationToken);
        var result = (await preview.Content.ReadFromJsonAsync<SearchProfilePreviewDto>(ApiJson, CancellationToken))!;

        result.Errors.Should().BeEmpty();
        result.Scored.Should().Be(2);
        result.Matching.Should().BeGreaterThan(result.CurrentMatching, "without the Java exclusion the Java job matches too");
        await using var db = Postgres.CreateDbContext();
        (await db.Vacancies.Select(v => v.RelevanceScore).ToListAsync(CancellationToken)).Should().BeEquivalentTo(new int?[] { 80, 10 }, "nothing was saved");
        (await GetAsync()).Version.Should().Be(1);

        dto.Profile.SearchTerms.Clear();
        var invalid = (await (await Client.PostAsJsonAsync("/api/search-profile/preview", dto.Profile, ApiJson, CancellationToken))
            .Content.ReadFromJsonAsync<SearchProfilePreviewDto>(ApiJson, CancellationToken))!;
        invalid.Errors.Should().Contain("Suchbegriffe: mindestens ein Suchbegriff ist nötig.");
    }

    [Fact]
    public async Task Export_downloads_the_profile_as_json()
    {
        var response = await Client.GetAsync("/api/search-profile/export", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName.Should().Be("suchprofil-v1.json");
        var exported = SearchProfileStore.Deserialize(await response.Content.ReadAsStringAsync(CancellationToken));
        SearchProfileRules.Validate(exported).Should().BeEmpty();
    }

    [Fact]
    public async Task The_import_runs_the_generated_searches_and_title_exclusions_from_the_suchprofil()
    {
        var dto = await GetAsync();
        dto.Profile.SearchTerms = [new SearchTerm { Text = "Blazor", Scope = SearchTermScope.Region }];
        dto.Profile.RemoteGermany = false;
        dto.Profile.TitleExclusions = ["Teilzeit"];
        var saved = await Client.PutAsJsonAsync("/api/search-profile", new SaveSearchProfileRequest(1, dto.Profile), ApiJson, CancellationToken);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(CancellationToken));
        _source.Returns("region-blazor", FakeVacancySource.Candidate("B-1", "Blazor Entwickler Teilzeit"), FakeVacancySource.Candidate("B-2", "Blazor Entwickler"));

        var run = await RunImportAsync();

        _source.Searches.Select(s => s.Name).Should().Equal("region-blazor");
        run.Should().BeEquivalentTo(new { Added = 1, Excluded = 1 });
    }

    [Fact]
    public async Task Min_score_top_sources_hidden_types_and_facets_in_the_list()
    {
        await SeedAsync(
            Scored(Make(title: "A"), 90),
            Scored(Make(title: "B", source: VacancySource.Himalayas), 70),
            Scored(Make(title: "C"), 55),
            Scored(Make(title: "D", employerType: EmployerType.Temporary), 95),
            Scored(Make(title: "E", source: VacancySource.Himalayas), 20));

        (await ListAsync("?minScore=60")).Items.Select(v => v.Title).Should().Equal("D", "A", "B");
        var top = await ListAsync("?top=2");
        top.Items.Select(v => v.Title).Should().Equal("D", "A");
        top.Should().BeEquivalentTo(new { TotalCount = 2, TotalMatching = 5, Top = 2 });
        (await ListAsync("?top=2&source=Himalayas")).Items.Select(v => v.Title).Should().Equal("B", "E");
        (await ListAsync("?source=Himalayas&source=Arbeitsagentur&minScore=0")).TotalCount.Should().Be(5);

        var all = await ListAsync("?source=Himalayas");
        all.Facets.Source.Should().BeEquivalentTo(new Dictionary<string, int> { ["Arbeitsagentur"] = 3, ["Himalayas"] = 2 },
            "facet counts ignore their own filter");
        all.Facets.EmployerType.Should().NotContainKey("Temporary", "the other filters (source) still apply");
        (await ListAsync(string.Empty)).Facets.EmployerType["Temporary"].Should().Be(1);

        // „Zeitarbeit ausblenden" in the Suchprofil
        var dto = await GetAsync();
        dto.Profile.Temporary = EmployerTypePolicy.Hide;
        await Client.PutAsJsonAsync("/api/search-profile", new SaveSearchProfileRequest(1, dto.Profile), ApiJson, CancellationToken);
        (await ListAsync(string.Empty)).Items.Select(v => v.Title).Should().NotContain("D");
        (await ListAsync("?employerType=Temporary")).Items.Select(v => v.Title).Should().BeEquivalentTo(["D"], "asked for explicitly");

        foreach (var bad in new[] { "?minScore=101", "?minScore=-1", "?top=0", "?top=500", "?source=LinkedIn" })
        {
            (await Client.GetAsync($"/api/vacancies{bad}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest, bad);
        }

        (await ListAsync("?minScore=100")).Items.Should().BeEmpty();
        (await ListAsync("?minScore=0")).TotalCount.Should().Be(4, "D is hidden now");
    }

    private static Vacancy Scored(Vacancy vacancy, int score)
    {
        vacancy.RelevanceScore = score;
        vacancy.RankScore = score;
        vacancy.RelevanceLevel = score >= 55 ? RelevanceLevel.Relevant : score >= 30 ? RelevanceLevel.Maybe : RelevanceLevel.Irrelevant;
        return vacancy;
    }

    private async Task SeedAsync(params Vacancy[] vacancies)
    {
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.AddRange(vacancies);
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<SearchProfileDto> GetAsync() =>
        (await Client.GetFromJsonAsync<SearchProfileDto>("/api/search-profile", ApiJson, CancellationToken))!;

    private async Task<List<SearchProfileRevisionInfo>> HistoryAsync() =>
        (await Client.GetFromJsonAsync<List<SearchProfileRevisionInfo>>("/api/search-profile/history", ApiJson, CancellationToken))!;

    private async Task<VacancyPageDto> ListAsync(string query) =>
        (await Client.GetFromJsonAsync<VacancyPageDto>($"/api/vacancies{query}", ApiJson, CancellationToken))!;
}
