using FluentAssertions;
using JobAutopilot.Core.Applications;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit.Applications;

public class ApplicationTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Status_changes_are_recorded_as_history_and_move_the_last_action()
    {
        var application = New(Monday);

        application.ChangeStatus(ApplicationStatus.Acknowledged, Monday.AddDays(2), "Eingangsbestätigung per E-Mail").Should().BeTrue();
        application.ChangeStatus(ApplicationStatus.Interview, Monday.AddDays(5)).Should().BeTrue();
        application.ChangeStatus(ApplicationStatus.Interview, Monday.AddDays(6)).Should().BeFalse("same status again");

        application.Events.Select(e => (e.From, e.To)).Should().Equal(
            (null, ApplicationStatus.Sent),
            (ApplicationStatus.Sent, ApplicationStatus.Acknowledged),
            (ApplicationStatus.Acknowledged, ApplicationStatus.Interview));
        application.Events[1].Note.Should().Be("Eingangsbestätigung per E-Mail");
        application.LastActionAt.Should().Be(Monday.AddDays(5));
    }

    [Theory]
    [InlineData(ApplicationStatus.Sent, true)]
    [InlineData(ApplicationStatus.Acknowledged, true)]
    [InlineData(ApplicationStatus.Response, true)]
    [InlineData(ApplicationStatus.Draft, false)]
    [InlineData(ApplicationStatus.Interview, false)]
    [InlineData(ApplicationStatus.Offer, false)]
    [InlineData(ApplicationStatus.Rejected, false)]
    [InlineData(ApplicationStatus.NoResponse, false)]
    [InlineData(ApplicationStatus.Withdrawn, false)]
    public void Follow_up_is_seven_days_after_the_last_action_only_while_open(ApplicationStatus status, bool hasFollowUp)
    {
        var application = New(Monday);
        application.ChangeStatus(status, Monday);

        application.FollowUpAt.Should().Be(hasFollowUp ? Monday.AddDays(7) : null);
    }

    [Fact]
    public void Follow_up_is_due_from_that_calendar_day_on()
    {
        var application = New(Monday);

        application.IsFollowUpDue(Monday.AddDays(6)).Should().BeFalse();
        application.IsFollowUpDue(new DateTimeOffset(2026, 10, 26, 0, 30, 0, TimeSpan.FromHours(1))).Should().BeTrue("26.10. in Germany");
        application.IsFollowUpDue(Monday.AddDays(30)).Should().BeTrue();
    }

    [Theory]
    [InlineData("Entwurf", ApplicationStatus.Draft)]
    [InlineData("Freigegeben", ApplicationStatus.Approved)]
    [InlineData("Gesendet", ApplicationStatus.Sent)]
    [InlineData("Eingangsbestätigung", ApplicationStatus.Acknowledged)]
    [InlineData("Rückmeldung", ApplicationStatus.Response)]
    [InlineData("Interview", ApplicationStatus.Interview)]
    [InlineData("Tech-Interview", ApplicationStatus.TechInterview)]
    [InlineData("Angebot", ApplicationStatus.Offer)]
    [InlineData("Absage", ApplicationStatus.Rejected)]
    [InlineData("Keine Antwort", ApplicationStatus.NoResponse)]
    [InlineData("Zurückgezogen", ApplicationStatus.Withdrawn)]
    [InlineData(" absage ", ApplicationStatus.Rejected)]
    [InlineData("Rueckmeldung", ApplicationStatus.Response)]
    public void Every_german_label_is_understood(string label, ApplicationStatus expected)
    {
        ApplicationStatuses.TryParseLabel(label, out var status).Should().BeTrue();
        status.Should().Be(expected);
        if (label == label.Trim() && !label.Contains("ue", StringComparison.Ordinal))
        {
            ApplicationStatuses.Label(expected).Should().Be(label, "round trip");
        }
    }

    [Fact]
    public void Marking_a_vacancy_as_applied_prefills_from_the_vacancy()
    {
        var vacancy = Vacancy.CreateManual("Full Stack Developer C# / Vue.js", "Förderwerk", Monday, location: "93049 Regensburg", url: "https://example.de/1");
        vacancy.Source = VacancySource.Arbeitsagentur;

        var application = Application.ForVacancy(vacancy, Monday);

        application.Should().BeEquivalentTo(new
        {
            VacancyId = vacancy.Id, Company = "Förderwerk", Position = "Full Stack Developer C# / Vue.js", Location = "93049 Regensburg",
            Channel = "Arbeitsagentur", Status = ApplicationStatus.Sent, AppliedAt = Monday,
        });
        application.Events.Should().ContainSingle().Which.Note.Should().Be("Als beworben markiert");
        application.DuplicateKey.Should().Be(DuplicateKeys.For("Förderwerk", "Full Stack Developer C# / Vue.js"));
    }

    [Theory]
    [InlineData("Förderwerk Logistics GmbH (Förderwerk Logistics Group)", "Full Stack Developer C# / Vue.js – Fokus Backend (m/w/d)",
        "Förderwerk", "Full Stack Developer C# / Vue.js - Fokus Backend, Lindenau", true)]
    [InlineData("Donau Business Software GmbH", "Softwareentwickler (m/w/d)", "Donau Business Software GmbH", "Donau Business Software GmbH: Softwareentwickler (m/w/d)", true)]
    [InlineData("Brückenwerk IT Services GmbH", "Senior Fullstack-Entwickler (m/w/d) – .NET & Angular oder Vue.js",
        "Brückenwerk IT Services GmbH", "Senior Fullstack-Entwickler (m/w/d) – .NET & Angular oder Vue.js", true)]
    [InlineData("Förderwerk Logistics GmbH", "Full Stack Developer C# / Vue.js", "Förderwerk Logistics GmbH", "Projektleiter Intralogistik (m/w/d)", false)]
    [InlineData("Brückenwerk IT Services GmbH", "Senior Fullstack-Entwickler (m/w/d) – .NET & Angular oder Vue.js",
        "Brückenwerk IT Services GmbH", "Senior Fullstack-Entwickler (m/w/d) – Go & Vue.js", false)]
    [InlineData("Brückenwerk IT Services GmbH", "Senior Fullstack-Entwickler (m/w/d) – .NET & Angular oder Vue.js",
        "Brückenwerk IT Services GmbH", "Senior Fullstack-Entwickler (m/w/d) – Java & Angular oder Vue.js", false)]
    [InlineData("synaforce GmbH", "Softwareentwickler (m/w/d)", "synaforce GmbH", "Softwareentwickler (m/w/d) - in Hofkirchen", true)]
    [InlineData("Canon Production Printing", "Software Developer (m/f/d)", "Canonical", "Software Developer", false)]
    [InlineData("MAX STREICHER GmbH & Co. KG aA", "Wirtschaftsinformatiker (m/w/d)", "Max Bögl", "Wirtschaftsinformatiker (m/w/d)", false)]
    [InlineData("msg (msg systems)", "Softwareentwickler", "msgGillardon", "Softwareentwickler", false)]
    public void Matches_applications_to_vacancies_by_company_and_similar_title(
        string company, string position, string vacancyCompany, string vacancyTitle, bool expected)
    {
        var application = New(Monday);
        application.Company = company;
        application.Position = position;
        application.RefreshDuplicateKey();
        var vacancy = Vacancy.CreateManual(vacancyTitle, vacancyCompany, Monday);
        vacancy.DuplicateKey = DuplicateKeys.For(vacancyCompany, vacancyTitle);

        ApplicationMatcher.Matches(application, vacancy).Should().Be(expected);
    }

    private static Application New(DateTimeOffset at)
    {
        var application = new Application
        {
            Id = Guid.CreateVersion7(at), Company = "Firma", Position = "Dev", AppliedAt = at, LastActionAt = at, CreatedAt = at, UpdatedAt = at,
        };
        application.ChangeStatus(ApplicationStatus.Sent, at);
        return application;
    }
}
