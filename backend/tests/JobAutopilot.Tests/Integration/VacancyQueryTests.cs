using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Vacancies;
using Microsoft.AspNetCore.Mvc;
using static JobAutopilot.Tests.Integration.VacancySeed;

namespace JobAutopilot.Tests.Integration;

/// <summary>Filters, sorting, paging envelope and validation of GET /api/vacancies against real PostgreSQL.</summary>
public class VacancyQueryTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly DateTimeOffset Day = new(2026, 10, 10, 0, 0, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData("entwickler", new[] { "Softwareentwickler", ".NET Entwickler", "Teilzeit 50% Entwickler_in" })]
    [InlineData("ENTWICKLER", new[] { "Softwareentwickler", ".NET Entwickler", "Teilzeit 50% Entwickler_in" })]
    [InlineData("muster", new[] { "Projektleiter" })]
    [InlineData("beispiel gmbh", new[] { "Softwareentwickler", ".NET Entwickler", "Tester" })]
    [InlineData("50%", new[] { "Teilzeit 50% Entwickler_in" })]
    [InlineData("_", new[] { "Teilzeit 50% Entwickler_in" })]
    public async Task Q_matches_title_or_company_case_insensitively_and_literally(string q, string[] expected)
    {
        await SeedAsync(
            Make(title: "Softwareentwickler"),
            Make(title: ".NET Entwickler"),
            Make(title: "Projektleiter", company: "Muster AG"),
            Make(title: "Teilzeit 50% Entwickler_in", company: "Firma"),
            Make(title: "Tester", company: "Beispiel GmbH"));

        var page = await GetAsync($"?q={Uri.EscapeDataString(q)}");

        page.Items.Select(v => v.Title).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task Location_matches_part_of_the_location()
    {
        await SeedAsync(
            Make(title: "A", location: "93047 Regensburg"),
            Make(title: "B", location: "94469 Deggendorf"),
            Make(title: "C", location: null));

        var page = await GetAsync("?location=regens");

        page.Items.Select(v => v.Title).Should().Equal("A");
    }

    [Fact]
    public async Task EmployerType_is_repeatable()
    {
        await SeedAsync(
            Make(title: "Direct", employerType: EmployerType.Direct),
            Make(title: "Agency", employerType: EmployerType.Agency),
            Make(title: "Temporary", employerType: EmployerType.Temporary),
            Make(title: "Unknown", employerType: EmployerType.Unknown));

        var one = await GetAsync("?employerType=Agency");
        var two = await GetAsync("?employerType=Direct&employerType=unknown");

        one.Items.Select(v => v.Title).Should().Equal("Agency");
        two.Items.Select(v => v.Title).Should().BeEquivalentTo("Direct", "Unknown");
    }

    [Theory]
    [InlineData("?seen=true", new[] { "Seen" })]
    [InlineData("?seen=false", new[] { "New" })]
    [InlineData("", new[] { "Seen", "New" })]
    public async Task Seen_filters_by_status(string query, string[] expected)
    {
        await SeedAsync(Make(title: "Seen", isSeen: true), Make(title: "New", isSeen: false));

        var page = await GetAsync(query);

        page.Items.Select(v => v.Title).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task Source_filters_by_origin()
    {
        await SeedAsync(
            Make(title: "AA", source: VacancySource.Arbeitsagentur),
            Make(title: "Manual", source: VacancySource.Manual));

        (await GetAsync("?source=Manual")).Items.Select(v => v.Title).Should().Equal("Manual");
        (await GetAsync("?source=Arbeitsagentur")).Items.Select(v => v.Title).Should().Equal("AA");
    }

    [Fact]
    public async Task Default_sort_is_newest_publication_first_with_missing_dates_last()
    {
        await SeedAsync(
            Make(title: "Old", publishedAt: Day.AddDays(-5), fetchedMinutesAfterBase: 3),
            Make(title: "NoDate", publishedAt: null, fetchedMinutesAfterBase: 9),
            Make(title: "Newest", publishedAt: Day, fetchedMinutesAfterBase: 1),
            Make(title: "Middle", publishedAt: Day.AddDays(-1), fetchedMinutesAfterBase: 2));

        var byDefault = await GetAsync(string.Empty);
        var explicitly = await GetAsync("?sort=published");

        byDefault.Items.Select(v => v.Title).Should().Equal("Newest", "Middle", "Old", "NoDate");
        explicitly.Items.Select(v => v.Title).Should().Equal(byDefault.Items.Select(v => v.Title));
    }

    [Fact]
    public async Task Sort_fetched_is_most_recently_imported_first()
    {
        await SeedAsync(
            Make(title: "First", publishedAt: Day, fetchedMinutesAfterBase: 1),
            Make(title: "Last", publishedAt: Day.AddDays(-9), fetchedMinutesAfterBase: 3),
            Make(title: "Second", publishedAt: null, fetchedMinutesAfterBase: 2));

        var page = await GetAsync("?sort=fetched");

        page.Items.Select(v => v.Title).Should().Equal("Last", "Second", "First");
    }

    [Fact]
    public async Task Sort_modes_order_by_combined_rule_or_ai_score()
    {
        static Vacancy Scored(string title, int rank, int rules, int? ai)
        {
            var vacancy = Make(title: title);
            (vacancy.RelevanceScore, vacancy.RankScore, vacancy.RuleRankScore, vacancy.AiScore) = (90, rank, rules, ai);
            return vacancy;
        }

        await SeedAsync(Scored("A", 80, 60, null), Scored("B", 70, 90, 50), Scored("C", 60, 70, 95));

        (await GetAsync("?sort=combined")).Items.Select(v => v.Title).Should().Equal("A", "B", "C");
        (await GetAsync("?sort=relevance")).Items.Select(v => v.Title).Should().Equal("A", "B", "C");
        (await GetAsync("?sort=rules")).Items.Select(v => v.Title).Should().Equal("B", "C", "A");
        (await GetAsync("?sort=ai")).Items.Select(v => v.Title).Should().Equal(["C", "B", "A"], "without a KI score at the end");
        (await GetAsync("?sort=rules&minScore=65")).Items.Select(v => v.Title).Should().Equal("B", "C");
    }

    [Fact]
    public async Task Filters_combine_and_the_envelope_counts_all_matches()
    {
        var matching = Enumerable.Range(1, 5)
            .Select(i => Make(title: $"Entwickler {i}", isSeen: false, publishedAt: Day.AddDays(-i)))
            .ToArray();
        await SeedAsync([
            .. matching,
            Make(title: "Entwickler gesehen", isSeen: true),
            Make(title: "Entwickler Agentur", employerType: EmployerType.Agency),
            Make(title: "Tester"),
        ]);

        var page = await GetAsync("?q=entwickler&seen=false&employerType=Unknown&page=2&pageSize=2");

        page.TotalCount.Should().Be(5);
        page.Page.Should().Be(2);
        page.PageSize.Should().Be(2);
        page.Items.Select(v => v.Title).Should().Equal("Entwickler 3", "Entwickler 4");
    }

    [Fact]
    public async Task Page_beyond_the_end_is_empty_but_keeps_the_total()
    {
        await SeedAsync(Make(), Make());

        var page = await GetAsync("?page=5&pageSize=2");

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(2);
    }

    [Theory]
    [InlineData("employerType=Recruiter", "EmployerType")]
    [InlineData("employerType=7", "EmployerType")]
    [InlineData("source=Website", "Source")]
    [InlineData("sort=title", "Sort")]
    [InlineData("page=0", "Page")]
    [InlineData("pageSize=101", "PageSize")]
    public async Task Invalid_parameters_return_validation_problems(string query, string field)
    {
        var response = await Client.GetAsync($"/api/vacancies?{query}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken);
        problem!.Errors.Keys.Should().Equal(field);
    }

    [Fact]
    public async Task Malformed_boolean_returns_bad_request()
    {
        var response = await Client.GetAsync("/api/vacancies?seen=maybe", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private async Task SeedAsync(params Vacancy[] vacancies)
    {
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.AddRange(vacancies);
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<PagedResponse<VacancyDto>> GetAsync(string query)
    {
        var page = await Client.GetFromJsonAsync<PagedResponse<VacancyDto>>(
            $"/api/vacancies{query}", JsonOptions, CancellationToken);
        return page!;
    }
}
