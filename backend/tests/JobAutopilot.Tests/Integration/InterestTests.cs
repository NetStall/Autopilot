using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Settings;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Vacancies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static JobAutopilot.Tests.Integration.VacancySeed;

namespace JobAutopilot.Tests.Integration;

/// <summary>Merken / Ausblenden, "new since last import" and the UI settings (task 03c, part C).</summary>
public class InterestTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    [Theory]
    [InlineData("Starred", VacancyInterest.Starred)]
    [InlineData("dismissed", VacancyInterest.Dismissed)]
    [InlineData("None", VacancyInterest.None)]
    public async Task Patch_sets_the_interest(string value, VacancyInterest expected)
    {
        var vacancy = Make();
        await SeedAsync(vacancy);

        var response = await PatchAsync(vacancy.Id, $$"""{ "interest": "{{value}}" }""");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadAsync(vacancy.Id)).Interest.Should().Be(expected);
    }

    [Fact]
    public async Task Patch_can_set_seen_and_interest_together_and_keeps_the_other_field()
    {
        var vacancy = Make(isSeen: false);
        await SeedAsync(vacancy);

        await PatchAsync(vacancy.Id, """{ "isSeen": true, "interest": "Starred" }""");
        await PatchAsync(vacancy.Id, """{ "isSeen": false }""");

        var stored = await LoadAsync(vacancy.Id);
        stored.IsSeen.Should().BeFalse();
        stored.Interest.Should().Be(VacancyInterest.Starred, "a PATCH without interest leaves it alone");
    }

    [Theory]
    [InlineData("""{ "interest": "Favourite" }""")]
    [InlineData("""{ "interest": "1" }""")]
    public async Task Unknown_interest_is_a_validation_problem(string body)
    {
        var vacancy = Make();
        await SeedAsync(vacancy);

        var response = await PatchAsync(vacancy.Id, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(CancellationToken);
        problem!.Errors.Keys.Should().Equal("Interest");
    }

    [Fact]
    public async Task Dismissed_vacancies_are_hidden_by_default()
    {
        await SeedInterestsAsync();

        var page = await GetAsync(string.Empty);

        page.Items.Select(v => v.Title).Should().BeEquivalentTo("none", "starred");
        page.TotalCount.Should().Be(2);
    }

    [Theory]
    [InlineData("?interest=Starred", new[] { "starred" })]
    [InlineData("?interest=Dismissed", new[] { "dismissed" })]
    [InlineData("?interest=Starred&interest=Dismissed", new[] { "starred", "dismissed" })]
    [InlineData("?includeDismissed=true", new[] { "none", "starred", "dismissed" })]
    [InlineData("?includeDismissed=false", new[] { "none", "starred" })]
    public async Task Interest_filters(string query, string[] expected)
    {
        await SeedInterestsAsync();

        var page = await GetAsync(query);

        page.Items.Select(v => v.Title).Should().BeEquivalentTo(expected);
        page.Items.Should().OnlyContain(v => v.Interest.ToString().Equals(v.Title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Invalid_interest_filter_is_rejected()
    {
        var response = await Client.GetAsync("/api/vacancies?interest=Maybe", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Fetched_since_returns_vacancies_from_the_last_import()
    {
        await SeedAsync(
            Make(title: "older", fetchedMinutesAfterBase: 0),
            Make(title: "from last import", fetchedMinutesAfterBase: 60),
            Make(title: "also new", fetchedMinutesAfterBase: 61));
        var since = Uri.EscapeDataString(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero).ToString("O"));

        var page = await GetAsync($"?fetchedSince={since}");

        page.Items.Select(v => v.Title).Should().BeEquivalentTo("from last import", "also new");
    }

    [Fact]
    public async Task Ui_settings_return_the_highlight_keywords()
    {
        var settings = await Client.GetFromJsonAsync<UiSettingsDto>("/api/settings/ui", ApiJson, CancellationToken);

        settings!.HighlightKeywords.Should().Contain(["C#", ".NET", "Vue", "Remote"]);
    }

    private async Task SeedInterestsAsync()
    {
        var none = Make(title: "none");
        var starred = Make(title: "starred");
        starred.Interest = VacancyInterest.Starred;
        var dismissed = Make(title: "dismissed");
        dismissed.Interest = VacancyInterest.Dismissed;
        await SeedAsync(none, starred, dismissed);
    }

    private async Task<HttpResponseMessage> PatchAsync(Guid id, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await Client.PatchAsync($"/api/vacancies/{id}", content, CancellationToken);
    }

    private async Task<Vacancy> LoadAsync(Guid id)
    {
        await using var db = Postgres.CreateDbContext();
        return await db.Vacancies.AsNoTracking().SingleAsync(v => v.Id == id, CancellationToken);
    }

    private async Task SeedAsync(params Vacancy[] vacancies)
    {
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.AddRange(vacancies);
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<PagedResponse<VacancyDto>> GetAsync(string query) =>
        (await Client.GetFromJsonAsync<PagedResponse<VacancyDto>>($"/api/vacancies{query}", ApiJson, CancellationToken))!;
}
