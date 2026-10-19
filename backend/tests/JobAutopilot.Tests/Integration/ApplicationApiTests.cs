using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using JobAutopilot.Api.Common;
using JobAutopilot.Api.Features.Applications;
using JobAutopilot.Api.Features.Vacancies;
using JobAutopilot.Core.Applications;
using JobAutopilot.Infrastructure.Applications;
using Microsoft.EntityFrameworkCore;
using static JobAutopilot.Tests.Integration.VacancySeed;

namespace JobAutopilot.Tests.Integration;

/// <summary>Applications through the API, the tracker import and the „Beworben" badge — against real PostgreSQL.</summary>
public sealed class ApplicationApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres), IDisposable
{
    private readonly string _trackerFile = Path.Combine(Path.GetTempPath(), $"tracker-{Guid.NewGuid():N}.json");

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Applications:TrackerImportPath"] = _trackerFile,
    };

    private const string Tracker = """
        { "applications": [
          { "id": "tgw", "datum": "2026-09-18", "firma": "TGW Logistics GmbH (TGW Logistics Group)",
            "position": "Full Stack Developer C# / Vue.js – Fokus Backend (m/w/d)", "kanal": "Firmenwebsite", "typ": "Direkt",
            "status": "Gesendet", "gehalt": 57000, "letzteAktion": "2026-09-18", "unterlagen": "CV.pdf, Anschreiben", "sprache": "DE" },
          { "id": "partspace", "datum": "2026-09-13", "firma": "PartSpace GmbH", "position": "Full Stack Software Entwickler (m/w/d)",
            "kanal": "DEVjobs.de", "status": "Absage", "letzteAktion": "2026-09-16" },
          { "id": "odd", "datum": "2026-09-14", "firma": "Bäckerei", "position": "IT-Mitarbeiter", "kanal": "Arbeitsagentur",
            "status": "Interview", "gehalt": 18 },
          { "id": "", "firma": "ohne id", "position": "x" }
        ] }
        """;

    [Fact]
    public async Task Tracker_import_is_idempotent_links_vacancies_and_reports_warnings()
    {
        var vacancy = Make(title: "Full Stack Developer C# / Vue.js - Fokus Backend,Teunz,Regensburg", company: "TGW");
        await SeedAsync(vacancy);
        await File.WriteAllTextAsync(_trackerFile, Tracker, Encoding.UTF8, CancellationToken);

        var first = await ImportAsync();
        var second = await ImportAsync();

        first.Should().BeEquivalentTo(new { Total = 4, Imported = 3, Updated = 0, Unchanged = 0, LinkedToVacancy = 1 });
        first.Warnings.Should().Contain(w => w.Contains("übersprungen", StringComparison.Ordinal))
            .And.Contain(w => w.Contains("Stundenlohn", StringComparison.Ordinal));
        second.Should().BeEquivalentTo(new { Total = 4, Imported = 0, Updated = 0, Unchanged = 3, LinkedToVacancy = 0 });

        await using var db = Postgres.CreateDbContext();
        var stored = await db.Applications.Include(a => a.Events).OrderBy(a => a.ExternalRef).ToListAsync(CancellationToken);
        stored.Select(a => a.ExternalRef).Should().Equal("odd", "partspace", "tgw");
        stored.Single(a => a.ExternalRef == "tgw").VacancyId.Should().Be(vacancy.Id);
        stored.Single(a => a.ExternalRef == "partspace").Status.Should().Be(ApplicationStatus.Rejected);
        stored.Should().OnlyContain(a => a.Events.Count == 1, "re-running added no history");
    }

    [Fact]
    public async Task Re_import_updates_changed_rows_but_keeps_a_newer_status_set_in_the_tool()
    {
        await File.WriteAllTextAsync(_trackerFile, Tracker, Encoding.UTF8, CancellationToken);
        await ImportAsync();
        var tgw = (await ListAsync(string.Empty)).Single(a => a.ExternalRef == "tgw");
        await PostStatusAsync(tgw.Id, ApplicationStatus.Interview, "Einladung");

        await File.WriteAllTextAsync(_trackerFile, Tracker.Replace("\"gehalt\": 57000", "\"gehalt\": 58000", StringComparison.Ordinal),
            Encoding.UTF8, CancellationToken);
        var report = await ImportAsync();

        report.Updated.Should().Be(1);
        var after = await Client.GetFromJsonAsync<ApplicationDto>($"/api/applications/{tgw.Id}", ApiJson, CancellationToken);
        after!.SalaryExpectation.Should().Be(58000);
        after.Status.Should().Be(ApplicationStatus.Interview, "the status set in the tool is newer than the export");
    }

    [Fact]
    public async Task Status_change_writes_history_resets_follow_up_and_filters_work()
    {
        Factory.Clock.Advance(TimeSpan.FromDays(10));
        await File.WriteAllTextAsync(_trackerFile, Tracker, Encoding.UTF8, CancellationToken);
        await ImportAsync();

        var due = await ListAsync("?followUpDue=true");
        due.Select(a => a.ExternalRef).Should().Equal("tgw");
        due[0].FollowUpAt.Should().NotBeNull();
        (await ListAsync("?status=Rejected")).Select(a => a.ExternalRef).Should().Equal("partspace");
        (await ListAsync("?channel=DEVjobs.de")).Select(a => a.ExternalRef).Should().Equal("partspace");

        var changed = await PostStatusAsync(due[0].Id, ApplicationStatus.Acknowledged, "Eingangsbestätigung");

        changed.StatusLabel.Should().Be("Eingangsbestätigung");
        changed.Events!.Select(e => e.ToLabel).Should().Equal("Eingangsbestätigung", "Gesendet");
        changed.IsFollowUpDue.Should().BeFalse("the follow-up date moved with the last action");
        (await ListAsync("?followUpDue=true")).Should().BeEmpty();
    }

    [Fact]
    public async Task Marking_a_vacancy_as_applied_creates_one_application_and_the_list_shows_beworben()
    {
        var vacancy = Make(title: "Softwareentwickler C# (m/w/d)", company: "Beispiel Software GmbH");
        var sameJobElsewhere = Make(title: "Softwareentwickler C# / .NET (w/m/d)", company: "Beispiel Software");
        var other = Make(title: "Andere Stelle", company: "Andere GmbH");
        await SeedAsync(vacancy, sameJobElsewhere, other);

        var created = await Client.PostAsync($"/api/vacancies/{vacancy.Id}/apply", content: null, CancellationToken);
        var again = await Client.PostAsync($"/api/vacancies/{vacancy.Id}/apply", content: null, CancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        again.StatusCode.Should().Be(HttpStatusCode.OK, "the existing application is returned, not a second one");
        var page = (await Client.GetFromJsonAsync<PagedResponse<VacancyDto>>("/api/vacancies", ApiJson, CancellationToken))!;
        page.Items.Single(v => v.Id == vacancy.Id).Applied!.StatusLabel.Should().Be("Gesendet");
        page.Items.Single(v => v.Id == sameJobElsewhere.Id).Applied.Should().NotBeNull("same company, similar title");
        page.Items.Single(v => v.Id == other.Id).Applied.Should().BeNull();
    }

    [Fact]
    public async Task Manual_crud_and_validation()
    {
        var response = await Client.PostAsJsonAsync("/api/applications",
            new { company = "Firma", position = "Dev", channel = "E-Mail", contactEmail = "not-an-email" }, ApiJson, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        response = await Client.PostAsJsonAsync("/api/applications", new { company = "Firma", position = "Dev", channel = "E-Mail" }, ApiJson, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<ApplicationDto>(ApiJson, CancellationToken))!;

        var patched = await Client.PatchAsJsonAsync($"/api/applications/{created.Id}",
            new { company = "Firma", position = "Senior Dev", notes = "Telefonat" }, ApiJson, CancellationToken);
        (await patched.Content.ReadFromJsonAsync<ApplicationDto>(ApiJson, CancellationToken))!.Position.Should().Be("Senior Dev");

        (await Client.DeleteAsync($"/api/applications/{created.Id}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetAsync($"/api/applications/{created.Id}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_missing_tracker_file_is_a_clear_400()
    {
        var response = await Client.PostAsync("/api/applications/import-tracker", content: null, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("Tracker-Export nicht gefunden");
    }

    public void Dispose() => File.Delete(_trackerFile);

    private async Task<TrackerImportReport> ImportAsync()
    {
        var response = await Client.PostAsync("/api/applications/import-tracker", content: null, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TrackerImportReport>(ApiJson, CancellationToken))!;
    }

    private async Task<List<ApplicationDto>> ListAsync(string query) =>
        (await Client.GetFromJsonAsync<List<ApplicationDto>>($"/api/applications{query}", ApiJson, CancellationToken))!;

    private async Task<ApplicationDto> PostStatusAsync(Guid id, ApplicationStatus status, string note)
    {
        var response = await Client.PostAsJsonAsync($"/api/applications/{id}/status", new { status, note }, ApiJson, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ApplicationDto>(ApiJson, CancellationToken))!;
    }

    private async Task SeedAsync(params Core.Vacancies.Vacancy[] vacancies)
    {
        await using var db = Postgres.CreateDbContext();
        foreach (var vacancy in vacancies)
        {
            vacancy.DuplicateKey = Core.Vacancies.DuplicateKeys.For(vacancy.Company, vacancy.Title);
        }

        db.Vacancies.AddRange(vacancies);
        await db.SaveChangesAsync(CancellationToken);
    }
}
