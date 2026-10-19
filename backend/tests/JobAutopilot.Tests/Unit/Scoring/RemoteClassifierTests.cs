using FluentAssertions;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit.Scoring;

/// <summary>The shipped remote phrases against realistic snippets.</summary>
public class RemoteClassifierTests
{
    private readonly RemoteClassifier _classifier = new(ShippedConfig.Remote());

    public static TheoryData<string, string?, RemoteLevel> Snippets => new()
    {
        { "Softwareentwickler C# (m/w/d)", "Die Stelle ist zu 100 % remote.", RemoteLevel.FullRemote },
        { "Softwareentwickler C# (m/w/d)", "Wir arbeiten vollständig remote und treffen uns zweimal im Jahr.", RemoteLevel.FullRemote },
        { ".NET Developer", "We are a remote-first company.", RemoteLevel.FullRemote },
        { "Backend Engineer", "Deutschlandweit remote – du arbeitest, wo du willst.", RemoteLevel.FullRemote },
        { "Fullstack Entwickler", "Homeoffice bis zu 100% möglich", RemoteLevel.FullRemote },
        { "Senior .NET Developer (Remote)", null, RemoteLevel.FullRemote },
        { "C# Entwickler - 100% Homeoffice", null, RemoteLevel.FullRemote },
        { "Softwareentwickler (m/w/d)", "Fully remote within Germany.", RemoteLevel.FullRemote },
        { "Softwareentwickler (m/w/d)", "Homeoffice möglich nach Einarbeitung.", RemoteLevel.Hybrid },
        { "Softwareentwickler (m/w/d)", "Flexible Arbeitszeiten und mobiles Arbeiten", RemoteLevel.Hybrid },
        { "Softwareentwickler (m/w/d)", "Hybrides Arbeiten: 2 Tage Homeoffice pro Woche", RemoteLevel.Hybrid },
        { "Softwareentwickler (m/w/d)", "bis zu 60 % Homeoffice", RemoteLevel.Hybrid },
        { "Software Engineer (hybrid)", null, RemoteLevel.Hybrid },
        { "Software Engineer - Remote/Hybrid", null, RemoteLevel.Hybrid },
        { "Softwareentwickler (m/w/d)", "Leider ist kein Homeoffice möglich.", RemoteLevel.OnSite },
        { "Softwareentwickler (m/w/d)", "Die Tätigkeit findet zu 100 % vor Ort statt.", RemoteLevel.OnSite },
        { "Softwareentwickler (m/w/d)", "Moderne Büros in Regensburg.", RemoteLevel.Unknown },
        { "Softwareentwickler (m/w/d)", null, RemoteLevel.Unknown },
        // Traps
        { "Netzwerktechniker (m/w/d)", "Hybridfahrzeuge und Remote-Zugriff auf Anlagen", RemoteLevel.Unknown },
    };

    [Theory]
    [MemberData(nameof(Snippets))]
    public void Snippets_get_the_expected_level(string title, string? description, RemoteLevel expected)
    {
        var result = _classifier.Classify(title, description);

        result.Level.Should().Be(expected, "reason: {0}", result.Reason);
    }

    [Fact]
    public void The_reason_names_the_phrase()
    {
        _classifier.Classify("Dev", "Wir arbeiten 100 % remote.").Reason.Should().Be("Voll remote: „100 % remote“");
        _classifier.Classify("Dev", "mobiles Arbeiten").Reason.Should().Be("Hybrid: „mobiles Arbeiten“");
        _classifier.Classify("Dev", null).Reason.Should().BeNull();
    }

    [Fact]
    public void Enricher_never_lowers_the_level_and_takes_the_source_hint()
    {
        var enricher = ShippedConfig.Enricher();
        var vacancy = Vacancy.CreateManual("Softwareentwickler", "Firma", DateTimeOffset.UnixEpoch);
        vacancy.RemoteLevel = RemoteLevel.Hybrid;
        vacancy.Description = "Leider kein Homeoffice.";

        enricher.Apply(vacancy);
        vacancy.RemoteLevel.Should().Be(RemoteLevel.Hybrid, "found by a remote search before");

        enricher.Apply(vacancy, RemoteLevel.FullRemote);
        vacancy.RemoteLevel.Should().Be(RemoteLevel.FullRemote);
        vacancy.RelevanceReasons.Should().Contain("Voll remote laut Quelle");
    }
}
