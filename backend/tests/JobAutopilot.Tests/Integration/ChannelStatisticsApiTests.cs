using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using JobAutopilot.Api.Features.Statistics;
using JobAutopilot.Core.Applications;

namespace JobAutopilot.Tests.Integration;

/// <summary>„Bewerbungswege" (08a): channel split of the visible list and the e-mail-ready vacancies.</summary>
public class ChannelStatisticsApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Counts_channels_and_lists_the_email_ready_vacancies_by_rank()
    {
        await SeedAsync("E-Mail", 90, "Bewirb dich per E-Mail an jobs@firma.de. Ansprechpartnerin: Frau Lisa Müller");
        await SeedAsync("Portal", 80, "Bewerbungen per E-Mail werden nicht berücksichtigt. Fragen an info@firma.de");
        await SeedAsync("Extern", 70, "Wir freuen uns auf dich.", "https://www.yourfirm.de/job/1");
        await SeedAsync("Unbekannt", 60, null);

        var stats = await Client.GetFromJsonAsync<ChannelStatisticsDto>("/api/statistics/channels?top=0", Json, CancellationToken);

        stats!.Vacancies.Should().Be(4);
        stats.Channels.Should().ContainEquivalentOf(new ChannelCountDto(ApplyChannel.Email, 1, 25))
            .And.ContainEquivalentOf(new ChannelCountDto(ApplyChannel.Portal, 1, 25))
            .And.ContainEquivalentOf(new ChannelCountDto(ApplyChannel.External, 1, 25))
            .And.ContainEquivalentOf(new ChannelCountDto(ApplyChannel.Unknown, 1, 25));
        stats.EmailReady.Should().Be(1);
        stats.TopEmailReady.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Title = "E-Mail", Email = "jobs@firma.de", ContactName = "Frau Lisa Müller" });

        (await Client.GetFromJsonAsync<ChannelStatisticsDto>("/api/statistics/channels?top=2", Json, CancellationToken))!.Vacancies.Should().Be(2);
        (await Client.GetAsync("/api/statistics/channels?top=-1", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task SeedAsync(string title, int rank, string? description, string? url = null)
    {
        var vacancy = VacancySeed.Make(title: title);
        (vacancy.Description, vacancy.RankScore, vacancy.RelevanceScore) = (description, rank, rank);
        // The Arbeitsagentur's own page means no external link (externeURL is empty).
        vacancy.Url = url ?? $"https://www.arbeitsagentur.de/jobsuche/jobdetail/{vacancy.ExternalId}";

        await using var db = Postgres.CreateDbContext();
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync(CancellationToken);
    }
}
