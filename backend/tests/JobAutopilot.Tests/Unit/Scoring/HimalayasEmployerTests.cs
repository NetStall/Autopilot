using FluentAssertions;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit.Scoring;

/// <summary>Himalayas lists the hiring company itself (05c): direct unless the text says agency.</summary>
public class HimalayasEmployerTests
{
    private static Vacancy Himalayas(string company, string description) => new()
    {
        Id = Guid.NewGuid(),
        Source = VacancySource.Himalayas,
        ExternalId = Guid.NewGuid().ToString("N"),
        Title = "Senior .NET Developer",
        Company = company,
        Url = "https://himalayas.app/jobs/x",
        Description = description,
    };

    [Fact]
    public void A_himalayas_job_without_agency_signals_is_a_direct_employer()
    {
        var vacancy = Himalayas("Pulsar Care", "Du entwickelst Backend Services mit C#.");

        ShippedConfig.Enricher().Apply(vacancy);

        vacancy.EmployerType.Should().Be(EmployerType.Direct);
        vacancy.RelevanceReasons.Should().Contain("Direkter Arbeitgeber laut Himalayas (einstellende Firma)");
    }

    [Fact]
    public void Agency_signals_still_win()
    {
        var vacancy = Himalayas("Example Talent", "Im Auftrag unseres Kunden suchen wir einen .NET Developer.");

        ShippedConfig.Enricher().Apply(vacancy);

        vacancy.EmployerType.Should().Be(EmployerType.Agency);
    }

    [Fact]
    public void Arbeitsagentur_jobs_without_flags_stay_unknown()
    {
        var vacancy = Himalayas("Beispiel GmbH", "Du entwickelst mit C#.");
        vacancy.Source = VacancySource.Arbeitsagentur;

        ShippedConfig.Enricher().Apply(vacancy);

        vacancy.EmployerType.Should().Be(EmployerType.Unknown);
    }
}
