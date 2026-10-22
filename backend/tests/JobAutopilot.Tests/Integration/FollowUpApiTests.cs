using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using JobAutopilot.Api.Features.Applications;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Documents;

namespace JobAutopilot.Tests.Integration;

/// <summary>„Follow-ups fällig" (08c): due 7 days after the last action while open; „Erledigt" moves it on.</summary>
public class FollowUpApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Open_applications_are_due_after_seven_days_and_done_moves_the_date()
    {
        var now = Factory.Clock.GetUtcNow();
        var due = await SeedAsync("Due GmbH", ApplicationStatus.Sent, now.AddDays(-8));
        await SeedAsync("Recent GmbH", ApplicationStatus.Sent, now.AddDays(-3));
        await SeedAsync("Rejected GmbH", ApplicationStatus.Rejected, now.AddDays(-20));

        var list = await Client.GetFromJsonAsync<List<FollowUpDto>>("/api/follow-ups", Json, CancellationToken);

        var item = list!.Should().ContainSingle().Subject;
        item.Company.Should().Be("Due GmbH");
        item.Draft.Subject.Should().Be("Nachfrage zu meiner Bewerbung als Entwickler (m/w/d)");
        item.Draft.Email.Should().Contain("am " + now.AddDays(-8).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture))
            .And.NotContainAny("Telefon", "telefonisch", "Anruf", "phone");

        (await Client.PostAsync($"/api/applications/{due}/follow-up-done", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetFromJsonAsync<List<FollowUpDto>>("/api/follow-ups", Json, CancellationToken)).Should().BeEmpty("the last action is today now");
    }

    [Fact]
    public void English_and_du_drafts_follow_the_application()
    {
        var application = new Application { Id = Guid.NewGuid(), Company = "Beispiel GmbH", Position = "Developer", AppliedAt = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), ContactName = "Anna Müller" };

        FollowUps.Draft(application, DocumentLanguage.En, Formality.Sie, "Alex").Email.Should().StartWith("Dear Anna Müller,").And.Contain("on 1 October 2026");
        FollowUps.Draft(application, DocumentLanguage.De, Formality.Du, "Alex").Email.Should().StartWith("Hallo Anna Müller,").And.Contain("bei euch");
        FollowUps.Draft(application, DocumentLanguage.De, Formality.Sie, "Alex").LinkedIn.Length.Should().BeLessThan(300);
    }

    private async Task<Guid> SeedAsync(string company, ApplicationStatus status, DateTimeOffset lastAction)
    {
        var application = new Application
        {
            Id = Guid.NewGuid(), Company = company, Position = "Entwickler (m/w/d)", Status = status, AppliedAt = lastAction, LastActionAt = lastAction,
            CreatedAt = lastAction, UpdatedAt = lastAction,
        };
        await using var db = Postgres.CreateDbContext();
        db.Applications.Add(application);
        await db.SaveChangesAsync(CancellationToken);
        return application.Id;
    }
}
