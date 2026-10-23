using FluentAssertions;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit.Scoring;

/// <summary>The shipped agency detection against realistic company names and snippets.</summary>
public class EmployerClassifierTests
{
    private readonly EmployerClassifier _classifier = new(ShippedConfig.Employer());

    public static TheoryData<string, string?, EmployerType, EmployerType> Cases => new()
    {
        // Source flags first
        { "Beispiel Engineering GmbH", null, EmployerType.Temporary, EmployerType.Temporary },
        { "Beispiel Recruiting GmbH", null, EmployerType.Agency, EmployerType.Agency },
        // Known agencies (whole words in the company)
        { "FERCHAU GmbH Niederlassung Rostock", null, EmployerType.Unknown, EmployerType.Agency },
        { "Hays AG", "Wir entwickeln Software.", EmployerType.Direct, EmployerType.Agency },
        { "Brunel GmbH", null, EmployerType.Unknown, EmployerType.Agency },
        { "Amadeus Fire AG", null, EmployerType.Unknown, EmployerType.Agency },
        { "Tröger & Cie. Aktiengesellschaft", null, EmployerType.Unknown, EmployerType.Agency },
        { "GULP – experts united", null, EmployerType.Unknown, EmployerType.Agency },
        { "Randstad Deutschland GmbH & Co. KG", null, EmployerType.Unknown, EmployerType.Agency },
        // Phrases
        { "Beispiel Consulting GmbH", "Im Auftrag unseres Kunden, eines Softwarehauses in Regensburg, suchen wir …", EmployerType.Unknown, EmployerType.Agency },
        { "Beispiel Consulting GmbH", "Für unseren Kunden suchen wir einen .NET-Entwickler.", EmployerType.Unknown, EmployerType.Agency },
        { "Beispiel Personal GmbH", "Als Personaldienstleister mit 20 Standorten …", EmployerType.Unknown, EmployerType.Agency },
        { "Beispiel Personal GmbH", "Die Stelle ist im Rahmen der Arbeitnehmerüberlassung zu besetzen.", EmployerType.Unknown, EmployerType.Temporary },
        { "Beispiel Personal GmbH", "Zeitarbeit mit Übernahmeoption", EmployerType.Unknown, EmployerType.Temporary },
        { "Example Talent Ltd", "On behalf of our client, a fintech in Berlin, we are looking for …", EmployerType.Unknown, EmployerType.Agency },
        // Direct employers
        { "Beispiel Software GmbH", "Wir entwickeln Software für unsere Kunden aus dem Mittelstand.", EmployerType.Unknown, EmployerType.Unknown },
        { "Beispiel Software GmbH", "Wir entwickeln Software für unsere Kunden.", EmployerType.Direct, EmployerType.Direct },
        { "Beispiel Software GmbH", "Wir bitten um keine Anfragen von Personaldienstleistern.", EmployerType.Unknown, EmployerType.Direct },
        { "Beispiel Software GmbH", "Bewerbungen bitte direkt – keine Personalvermittler.", EmployerType.Unknown, EmployerType.Direct },
        // Traps: names that contain an agency name inside a word
        { "Haysmann Elektrotechnik GmbH", null, EmployerType.Unknown, EmployerType.Unknown },
        { "Brunello Feinkost", null, EmployerType.Unknown, EmployerType.Unknown },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Detects_the_employer_type(string company, string? description, EmployerType hint, EmployerType expected)
    {
        var result = _classifier.Classify(company, description, hint);

        result.Type.Should().Be(expected, "reason: {0}", result.Reason);
    }

    [Fact]
    public void Reasons_name_the_signal()
    {
        _classifier.Classify("FERCHAU GmbH", null).Reason.Should().Be("Bekannter Personaldienstleister: FERCHAU");
        _classifier.Classify("X", null, EmployerType.Temporary).Reason.Should().Contain("Arbeitnehmerüberlassung");
        _classifier.Classify("X", "im Auftrag unseres Kunden").Reason.Should().Be("Personalvermittlung: „im Auftrag unseres Kunden“");
    }

    [Fact]
    public void Enricher_ranks_agencies_and_keeps_manual_types_and_stored_flags()
    {
        var enricher = ShippedConfig.Enricher();

        var agency = Imported("Softwareentwickler C#", "Hays AG");
        enricher.Apply(agency);
        agency.EmployerType.Should().Be(EmployerType.Agency);
        agency.RelevanceReasons.Should().Contain("Bekannter Personaldienstleister: Hays");

        var temporary = Imported("Softwareentwickler C#", "Beispiel Engineering GmbH");
        enricher.Apply(temporary, employerHint: EmployerType.Temporary);
        enricher.Apply(temporary); // re-scoring without the source's flags
        temporary.EmployerType.Should().Be(EmployerType.Temporary);
        temporary.RelevanceReasons.Should().Contain("Zeitarbeit laut Quelle");

        var manual = Vacancy.CreateManual("Softwareentwickler", "Hays AG", DateTimeOffset.UnixEpoch, employerType: EmployerType.Direct);
        enricher.Apply(manual);
        manual.EmployerType.Should().Be(EmployerType.Direct, "the candidate's own choice is kept");
    }

    private static Vacancy Imported(string title, string company)
    {
        var vacancy = Vacancy.CreateManual(title, company, DateTimeOffset.UnixEpoch);
        vacancy.Source = VacancySource.Arbeitsagentur;
        return vacancy;
    }
}
