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
    public void Explicit_text_beats_a_wrong_100_percent_field_ifm_prover()
    {
        // Real case (Arbeitsagentur 12511-2026X0000053169-S): homeofficeprozent = 100, the text says „anteilig".
        var vacancy = Imported(
            "Softwareentwickler für Tools und Anwendungen / C# (m/w/d)",
            "Wir bieten: flexible Arbeitszeiten (Gleitzeit) - anteilig Homeoffice - 30 Tage Urlaub");

        ShippedConfig.Enricher().Apply(vacancy, RemoteLevel.FullRemote);

        vacancy.RemoteLevel.Should().Be(RemoteLevel.Hybrid);
        vacancy.RemoteHint.Should().Be(RemoteLevel.FullRemote, "the source's value is kept, the text decides");
        vacancy.RelevanceReasons.Should().Contain("Hybrid: „anteilig Homeoffice“");
    }

    [Fact]
    public void Rescoring_lowers_the_level_when_the_rules_say_less_but_a_remote_search_stays_sticky()
    {
        var vacancy = Imported("Softwareentwickler", "Leider kein Homeoffice.");
        vacancy.RemoteLevel = RemoteLevel.FullRemote; // from an older rule set

        ShippedConfig.Enricher().Apply(vacancy);
        vacancy.RemoteLevel.Should().Be(RemoteLevel.OnSite, "re-scoring recomputes from the current text");

        vacancy.MarkFoundBy("remote-csharp", remoteOnly: true);
        ShippedConfig.Enricher().Apply(vacancy);
        vacancy.RemoteLevel.Should().Be(RemoteLevel.Hybrid, "found by a remote-only search: at least hybrid");
    }

    [Fact]
    public void Without_a_phrase_the_source_decides()
    {
        var vacancy = Imported("Softwareentwickler", "Wir freuen uns auf dich.");

        ShippedConfig.Enricher().Apply(vacancy, RemoteLevel.FullRemote);

        vacancy.RemoteLevel.Should().Be(RemoteLevel.FullRemote);
        vacancy.RelevanceReasons.Should().Contain("Voll remote laut Quelle");
    }

    private static Vacancy Imported(string title, string description)
    {
        var vacancy = Vacancy.CreateManual(title, "Firma", DateTimeOffset.UnixEpoch, description: description);
        vacancy.Source = VacancySource.Arbeitsagentur;
        return vacancy;
    }
}
