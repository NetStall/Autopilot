using FluentAssertions;
using JobAutopilot.Core.Applications;

namespace JobAutopilot.Tests.Unit.Applications;

public class TrackerMapperTests
{
    private static TrackerEntry Entry(string status = "Gesendet", string kanal = "LinkedIn", decimal? gehalt = 55000) => new()
    {
        Id = "beispiel-1",
        Datum = "2026-10-13",
        Firma = "Beispiel GmbH",
        Position = "Softwareentwickler (m/w/d)",
        Ort = "Regensburg, hybrid",
        Kanal = kanal,
        Typ = "Direkt",
        Status = status,
        Gehalt = gehalt,
        Kontakt = "",
        KontaktEmail = "jobs@example.org",
        Link = "https://example.org/jobs/1",
        LetzteAktion = "2026-10-16",
        NaechsterSchritt = "Follow-up am 23.10.",
        Notizen = "Lücken offen benannt.",
        Unterlagen = "Lebenslauf.pdf, Anschreiben.pdf ,",
        Sprache = "DE",
    };

    [Fact]
    public void Maps_every_field()
    {
        var (mapping, error) = TrackerMapper.Map(Entry());

        error.Should().BeNull();
        mapping!.Should().BeEquivalentTo(new
        {
            ExternalRef = "beispiel-1", Company = "Beispiel GmbH", Position = "Softwareentwickler (m/w/d)",
            Location = "Regensburg, hybrid", Channel = "LinkedIn", ContactName = (string?)null, ContactEmail = "jobs@example.org",
            Url = "https://example.org/jobs/1", Status = ApplicationStatus.Sent, SalaryExpectation = 55000,
            NextStep = "Follow-up am 23.10.",
        });
        mapping.Documents.Should().Equal("Lebenslauf.pdf", "Anschreiben.pdf");
        mapping.AppliedAt.Should().Be(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(2)));
        mapping.LastActionAt.Should().Be(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(2)));
        mapping.Notes.Should().Be("Typ: Direkt\nSprache: DE\nLücken offen benannt.");
        mapping.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_status_and_channel_go_into_notes_and_warnings_without_crashing()
    {
        var (mapping, _) = TrackerMapper.Map(Entry(status: "Warte auf Godot", kanal: "Brieftaube"));

        mapping!.Status.Should().Be(ApplicationStatus.Sent);
        mapping.Channel.Should().BeNull();
        mapping.Notes.Should().Contain("Status laut Tracker: Warte auf Godot").And.Contain("Kanal laut Tracker: Brieftaube");
        mapping.Warnings.Should().HaveCount(2);
    }

    [Fact]
    public void An_hourly_wage_is_not_taken_as_yearly_salary()
    {
        var (mapping, _) = TrackerMapper.Map(Entry(gehalt: 18));

        mapping!.SalaryExpectation.Should().BeNull();
        mapping.Notes.Should().Contain("Gehalt laut Tracker: 18 (vermutlich €/Stunde)");
        mapping.Warnings.Should().ContainSingle();
    }

    [Fact]
    public void Rows_without_id_company_or_position_are_skipped_with_a_reason()
    {
        var (mapping, error) = TrackerMapper.Map(new TrackerEntry { Id = "x", Firma = "Firma" });

        mapping.Should().BeNull();
        error.Should().Contain("übersprungen");
    }

    [Theory]
    [InlineData("Absage", ApplicationStatus.Rejected)]
    [InlineData("Interview", ApplicationStatus.Interview)]
    [InlineData("Keine Antwort", ApplicationStatus.NoResponse)]
    public void Status_labels_are_mapped(string label, ApplicationStatus expected) =>
        TrackerMapper.Map(Entry(status: label)).Mapping!.Status.Should().Be(expected);
}
