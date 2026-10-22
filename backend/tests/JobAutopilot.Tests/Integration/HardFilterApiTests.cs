using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using JobAutopilot.Api.Features.Statistics;
using JobAutopilot.Core.Filters;

namespace JobAutopilot.Tests.Integration;

/// <summary>Layer 1 (08b): filtered vacancies are hidden with their reason; an application or my „Passt" always keeps one visible.</summary>
public class HardFilterApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Filtered_vacancies_are_hidden_except_with_an_application_or_my_passt_label()
    {
        var shown = await SeedAsync("Shown", null);
        var role = await SeedAsync("Werkstudent", HardFilterReason.RoleType);
        var labelled = await SeedAsync("Labelled", HardFilterReason.GermanC1);
        var applied = await SeedAsync("Applied", HardFilterReason.Stack);
        await Client.PutAsJsonAsync($"/api/vacancies/{labelled.Id}/label", new { fits = true }, CancellationToken);
        await using (var db = Postgres.CreateDbContext())
        {
            db.Applications.Add(new Core.Applications.Application
            {
                Id = Guid.NewGuid(), VacancyId = applied.Id, Company = "Beispiel GmbH", Position = "Entwickler", Status = Core.Applications.ApplicationStatus.Sent,
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        var list = await Client.GetFromJsonAsync<JsonElement>("/api/vacancies?minScore=0", CancellationToken);
        var ids = list.GetProperty("items").EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToList();
        ids.Should().BeEquivalentTo([shown.Id, labelled.Id, applied.Id], "the Werkstudent job is filtered; labelled and applied ones never are");

        var all = await Client.GetFromJsonAsync<JsonElement>("/api/vacancies?minScore=0&includeFiltered=true", CancellationToken);
        var werkstudent = all.GetProperty("items").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == role.Id);
        werkstudent.GetProperty("filterReason").GetString().Should().Be("RoleType");
        werkstudent.GetProperty("filterNote").GetString().Should().Be("Rolle: „Werkstudent“");

        var stats = await Client.GetFromJsonAsync<FilterStatisticsDto>("/api/statistics/filters", Json, CancellationToken);
        stats!.Should().BeEquivalentTo(new { Vacancies = 4, Filtered = 1, Exempt = 2 });
        stats.Reasons.Should().HaveCount(3);
        stats.SavedSeconds.Should().BeGreaterThan(0);
    }

    private async Task<Core.Vacancies.Vacancy> SeedAsync(string title, HardFilterReason? reason)
    {
        var vacancy = VacancySeed.Make(title: title);
        (vacancy.RankScore, vacancy.RelevanceScore, vacancy.FilterReason) = (50, 50, reason);
        vacancy.FilterNote = reason == HardFilterReason.RoleType ? "Rolle: „Werkstudent“" : reason?.ToString();
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync(CancellationToken);
        return vacancy;
    }
}
