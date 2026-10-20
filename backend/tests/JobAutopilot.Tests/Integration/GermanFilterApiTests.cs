using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.SearchProfiles;
using JobAutopilot.Api.Features.Statistics;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Vacancies;
using static JobAutopilot.Tests.Integration.VacancySeed;

namespace JobAutopilot.Tests.Integration;

/// <summary>„Deutsch erforderlich": filter, facet counts, Suchprofil default and the statistics (step 05c).</summary>
public class GermanFilterApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static Vacancy Seeded(string title, RequiredGerman german, int score, RemoteLevel remote = RemoteLevel.Hybrid, VacancySource source = VacancySource.Arbeitsagentur)
    {
        var vacancy = Make(title: title, source: source, remoteLevel: remote);
        vacancy.RequiredGerman = german;
        vacancy.RelevanceScore = score;
        vacancy.RankScore = score;
        vacancy.RelevanceLevel = score >= 55 ? RelevanceLevel.Relevant : score >= 30 ? RelevanceLevel.Maybe : RelevanceLevel.Irrelevant;
        return vacancy;
    }

    private async Task SeedAsync()
    {
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.AddRange(
            Seeded("A", RequiredGerman.C1Plus, 90),
            Seeded("B", RequiredGerman.B2, 80, RemoteLevel.FullRemote),
            Seeded("C", RequiredGerman.B2, 70),
            Seeded("D", RequiredGerman.B1OrLower, 60, RemoteLevel.FullRemote, VacancySource.Himalayas),
            Seeded("E", RequiredGerman.None, 50, RemoteLevel.FullRemote, VacancySource.Himalayas),
            Seeded("F", RequiredGerman.Unknown, 45),
            Seeded("G", RequiredGerman.B2, 10));
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>The list's default relevance levels, as the UI sends them.</summary>
    private const string Default = "&relevance=Relevant&relevance=Maybe";

    private async Task<VacancyPageDto> ListAsync(string query) =>
        (await Client.GetFromJsonAsync<VacancyPageDto>($"/api/vacancies{query}", ApiJson, CancellationToken))!;

    [Fact]
    public async Task Filters_by_the_required_german_with_repeated_or_comma_separated_values()
    {
        await SeedAsync();

        (await ListAsync("?german=B2,B1OrLower" + Default)).Items.Select(v => v.Title).Should().Equal("B", "C", "D");
        (await ListAsync("?german=B2&german=B1OrLower" + Default)).Items.Select(v => v.Title).Should().Equal("B", "C", "D");
        (await ListAsync("?german=None")).Items.Should().ContainSingle().Which.RequiredGerman.Should().Be(RequiredGerman.None);
    }

    [Fact]
    public async Task Facets_count_the_german_levels_under_the_other_filters()
    {
        await SeedAsync();

        var page = await ListAsync("?german=C1Plus" + Default);
        page.Facets.RequiredGerman.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["C1Plus"] = 1, ["B2"] = 2, ["B1OrLower"] = 1, ["None"] = 1, ["Unknown"] = 1,
        }, "its own filter is ignored, the relevance filter (Passt + Vielleicht) is not");

        (await ListAsync("?german=C1Plus&source=Himalayas" + Default)).Facets.RequiredGerman
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["B1OrLower"] = 1, ["None"] = 1 });
    }

    [Theory]
    [InlineData("?german=A2")]
    [InlineData("?german=B2,Fluent")]
    [InlineData("?german=4")]
    public async Task Unknown_values_are_rejected(string query)
    {
        var response = await Client.GetAsync($"/api/vacancies{query}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("is not a German level");
    }

    [Fact]
    public async Task The_suchprofil_keeps_a_default_for_the_filter()
    {
        var dto = (await Client.GetFromJsonAsync<SearchProfileDto>("/api/search-profile", ApiJson, CancellationToken))!;
        dto.Profile.GermanFilter.Should().BeEmpty();
        dto.Profile.GermanFilter = [RequiredGerman.B2, RequiredGerman.B1OrLower, RequiredGerman.None];

        var saved = await Client.PutAsJsonAsync("/api/search-profile", new SaveSearchProfileRequest(dto.Version, dto.Profile), ApiJson, CancellationToken);

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetFromJsonAsync<SearchProfileDto>("/api/search-profile", ApiJson, CancellationToken))!.Profile.GermanFilter
            .Should().Equal(RequiredGerman.B2, RequiredGerman.B1OrLower, RequiredGerman.None);
    }

    [Fact]
    public async Task Statistics_show_the_distribution_of_relevant_vacancies_overall_by_remote_level_and_source()
    {
        await SeedAsync();
        // Not saved here: saving re-scores in the background and would replace the seeded scores.
        var profile = (await Client.GetFromJsonAsync<SearchProfileDto>("/api/search-profile", ApiJson, CancellationToken))!.Profile;

        var stats = (await Client.GetFromJsonAsync<GermanStatisticsDto>("/api/statistics/german", ApiJson, CancellationToken))!;

        // Passt + Vielleicht and at least the Mindestbewertung (all relevant ones score 45+): G (10) is not relevant.
        stats.MinScore.Should().Be(profile.MinScore);
        stats.Total.Should().Be(6);
        stats.Overall.Select(l => (l.Level, l.Count)).Should().Equal(
            (RequiredGerman.C1Plus, 1), (RequiredGerman.B2, 2), (RequiredGerman.B1OrLower, 1), (RequiredGerman.None, 1), (RequiredGerman.Unknown, 1));
        stats.Overall.Single(l => l.Level == RequiredGerman.B2).Should().BeEquivalentTo(new { Label = "B2", Percent = 33.3 });

        var fullRemote = stats.ByRemoteLevel.Single(b => b.Key == "FullRemote");
        fullRemote.Total.Should().Be(3);
        fullRemote.Levels.Single(l => l.Level == RequiredGerman.None).Percent.Should().Be(33.3);
        stats.BySource.Single(b => b.Key == "Himalayas").Levels.Where(l => l.Count > 0).Select(l => l.Level)
            .Should().Equal(RequiredGerman.B1OrLower, RequiredGerman.None);
    }
}
