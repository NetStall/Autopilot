using FluentAssertions;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit.Scoring;

/// <summary>The shipped relevance rules against realistic German and English postings.</summary>
public class RelevanceScorerTests
{
    private readonly RelevanceScorer _scorer = new(ShippedConfig.Relevance());

    public static TheoryData<string, string?, RelevanceLevel> Postings => new()
    {
        // The profile: C#, .NET, ASP.NET, EF, SQL, Vue, TypeScript
        { "Softwareentwickler C# / .NET (m/w/d)", null, RelevanceLevel.Relevant },
        { "Fullstack-Entwickler .NET & Vue.js (m/w/d)", "Sie entwickeln Web-APIs mit ASP.NET Core und Entity Framework.", RelevanceLevel.Relevant },
        { "Backend Developer (m/f/d)", "Our stack: C#, .NET 8, PostgreSQL, Docker, Azure. REST APIs for our SaaS.", RelevanceLevel.Relevant },
        { "Software Engineer", "Wir arbeiten mit TypeScript, Vue 3 und einem .NET-Backend auf MS SQL Server.", RelevanceLevel.Relevant },
        { "Teamleiter Softwareentwicklung .NET (m/w/d)", null, RelevanceLevel.Maybe },
        // Either / or with the core stack must stay
        { "Softwareentwickler Java oder C# (m/w/d)", null, RelevanceLevel.Maybe },
        { "Backend-Entwickler (m/w/d) C#, Java", "Sie programmieren in C# oder Java; Python-Kenntnisse sind von Vorteil.", RelevanceLevel.Maybe },
        // Nothing from the stack: generic terms alone must stay low
        { "Junior Fullstack Entwickler Fokus Next.js", "React, Next.js, Node.js und GraphQL", RelevanceLevel.Irrelevant },
        { "Senior Software Engineer / Fullstack", "Go, Kubernetes, gRPC", RelevanceLevel.Irrelevant },
        { "Softwareentwickler (m/w/d)", "Wir entwickeln mit C#/.NET. Kenntnisse in Python sind ein Plus.", RelevanceLevel.Relevant },
        // Generic titles without text: not hidden
        { "Softwareentwickler (m/w/d)", null, RelevanceLevel.Maybe },
        { "Anwendungsentwickler (m/w/d)", null, RelevanceLevel.Maybe },
        // Other main languages / fields
        { "Java-Entwickler (m/w/d)", "Spring Boot, Microservices, Kafka. C#-Kenntnisse wären ein Plus.", RelevanceLevel.Irrelevant },
        { "Senior Java Developer (all genders)", null, RelevanceLevel.Irrelevant },
        { "Python Developer (m/w/d)", "Django, FastAPI, PostgreSQL", RelevanceLevel.Irrelevant },
        { "PHP-Entwickler Laravel (m/w/d)", null, RelevanceLevel.Irrelevant },
        { "SAP ABAP Entwickler (m/w/d)", null, RelevanceLevel.Irrelevant },
        { "SAP Berater S/4HANA (m/w/d)", "Customizing im Modul SD", RelevanceLevel.Irrelevant },
        { "Embedded Software Engineer C++ (m/w/d)", "Firmware für Mikrocontroller, RTOS", RelevanceLevel.Irrelevant },
        { "SPS-Programmierer (m/w/d)", "Siemens TIA Portal, Step 7", RelevanceLevel.Irrelevant },
        { "Go Developer (m/w/d)", "Golang, Kubernetes", RelevanceLevel.Irrelevant },
        { "Ruby on Rails Developer", null, RelevanceLevel.Irrelevant },
        { "Softwareentwickler (m/w/d)", "Sie entwickeln in Java mit Spring Boot und Kotlin.", RelevanceLevel.Irrelevant },
        // Not a developer job at all
        { "Berufskraftfahrer CE (m/w/d)", "Führerschein Klasse C/CE erforderlich", RelevanceLevel.Irrelevant },
        { "Vertriebsmitarbeiter Innendienst (m/w/d)", null, RelevanceLevel.Irrelevant },
        // Working students / interns, even with C#
        { "Werkstudent Softwareentwicklung C# (m/w/d)", null, RelevanceLevel.Irrelevant },
        { "Praktikum .NET Entwicklung", null, RelevanceLevel.Irrelevant },
    };

    [Theory]
    [MemberData(nameof(Postings))]
    public void Realistic_postings_get_the_expected_level(string title, string? description, RelevanceLevel expected)
    {
        var result = _scorer.Score(title, description);

        result.Level.Should().Be(expected, "score {0}, reasons: {1}", result.Score, string.Join(" | ", result.Reasons));
    }

    [Fact]
    public void Related_skills_are_capped_per_group()
    {
        var result = _scorer.Score("Senior .NET / C# Entwickler ASP.NET Core", "Entity Framework, Blazor");

        result.Reasons.Should().Contain(r => r.StartsWith("Gruppe dotnet gedeckelt auf 50", StringComparison.Ordinal));
        result.Score.Should().BeLessThan(100);
    }

    [Fact]
    public void Generic_terms_without_the_stack_stay_far_below_maybe()
    {
        _scorer.Score("Fullstack Software Engineer", "Wir suchen Verstärkung für unser Team.").Score.Should().BeLessThan(25);
    }

    [Fact]
    public void Javascript_is_not_java()
    {
        var result = _scorer.Score("JavaScript-Entwickler (m/w/d)", "Frontend mit TypeScript und Vue.js");

        result.Reasons.Should().NotContain(r => r.StartsWith("Java ", StringComparison.Ordinal));
        result.Level.Should().NotBe(RelevanceLevel.Irrelevant);
    }

    [Fact]
    public void Netzwerk_and_domains_are_not_dotnet()
    {
        var result = _scorer.Score("Netzwerkadministrator (m/w/d)", "Bewerbung an jobs@firma.net");

        result.Reasons.Should().NotContain(r => r.StartsWith(".NET", StringComparison.Ordinal));
        result.Level.Should().Be(RelevanceLevel.Irrelevant);
    }

    [Fact]
    public void Title_matches_weigh_more_than_description_matches()
    {
        var inTitle = _scorer.Score("Entwickler C#", "Team");
        var inText = _scorer.Score("Entwickler", "Wir nutzen C#.");

        inTitle.Score.Should().BeGreaterThan(inText.Score);
        inTitle.Reasons.Should().Contain("C# im Titel ✓ (+50)");
        inText.Reasons.Should().Contain("C# ✓ (+25)");
    }

    [Fact]
    public void Reasons_explain_the_score_in_german_with_the_strongest_first()
    {
        var result = _scorer.Score("Java-Entwickler (m/w/d)", "Spring Boot. C# von Vorteil.");

        result.Reasons[0].Should().Be("Java als Hauptsprache im Titel ✗ (-60)");
        result.Reasons.Should().Contain("C# ✓ (+25)");
        result.Reasons.Should().Contain(r => r.StartsWith("Spring neben C#/.NET – abgeschwächt", StringComparison.Ordinal));
    }

    [Fact]
    public void Either_or_softens_the_other_language()
    {
        var result = _scorer.Score("Softwareentwickler Java oder C# (m/w/d)", null);

        result.Reasons.Should().Contain("Java neben C#/.NET im Titel – abgeschwächt (-15)");
    }

    [Fact]
    public void Leadership_roles_cost_points_but_stay_visible()
    {
        var plain = _scorer.Score("Softwareentwickler .NET (m/w/d)", null);
        var lead = _scorer.Score("Teamleiter Softwareentwicklung .NET (m/w/d)", null);

        (plain.Score - lead.Score).Should().Be(20);
        lead.Level.Should().Be(RelevanceLevel.Maybe);
        lead.Reasons.Should().Contain("Leitungsrolle („Teamleiter“) im Titel ✗ (-20)", "the reason names the text that matched");
    }

    [Theory]
    [InlineData("CRM Software Developer Microsoft Dynamics 365 (m/w/d)", "C# und .NET für Erweiterungen")]
    [InlineData("Web Developer Trainer (m/w/d)", "Schulungen zu C#, .NET und Vue")]
    [InlineData("Qualitätsingenieur / Testautomatisierungs-Entwickler (m/w/d)", "C# und .NET")]
    public void Neighbouring_roles_with_the_same_stack_are_not_relevant(string title, string description) =>
        _scorer.Score(title, description).Level.Should().NotBe(RelevanceLevel.Relevant);

    [Fact]
    public void Ausbildung_as_a_requirement_in_the_text_does_not_count()
    {
        var result = _scorer.Score("Softwareentwickler C# (m/w/d)", "Abgeschlossene Ausbildung als Fachinformatiker oder Studium.");

        result.Reasons.Should().NotContain(r => r.StartsWith("Ausbildung", StringComparison.Ordinal));
        result.Level.Should().Be(RelevanceLevel.Relevant);
    }

    [Fact]
    public void Without_description_the_reasons_say_so()
    {
        _scorer.Score("Softwareentwickler", null).Reasons.Should().Contain("Nur der Titel bewertet (noch keine Beschreibung)");
        _scorer.Score("Softwareentwickler", "Text").Reasons.Should().NotContain(r => r.StartsWith("Nur der Titel", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, RelevanceLevel.Irrelevant)]
    [InlineData(29, RelevanceLevel.Irrelevant)]
    [InlineData(30, RelevanceLevel.Maybe)]
    [InlineData(54, RelevanceLevel.Maybe)]
    [InlineData(55, RelevanceLevel.Relevant)]
    [InlineData(100, RelevanceLevel.Relevant)]
    public void Thresholds_come_from_the_options(int score, RelevanceLevel expected) =>
        _scorer.LevelFor(score).Should().Be(expected);

    [Fact]
    public void Score_is_clamped_and_weights_come_from_the_options()
    {
        var options = new RelevanceOptions
        {
            BaseScore = 50,
            TitleFactor = 3,
            Positive = { new RelevanceRule { Name = "Vue", Terms = { "Vue" }, Weight = 10 } },
            Negative = { new RelevanceRule { Name = "Cobol", Terms = { "COBOL" }, Weight = 40 } },
        };
        var scorer = new RelevanceScorer(options);

        scorer.Score("Vue Entwickler", null).Score.Should().Be(80);
        scorer.Score("Entwickler", "Vue").Score.Should().Be(60);
        scorer.Score("COBOL Entwickler", null).Score.Should().Be(0, "clamped at 0");
        scorer.Score("Vue Vue Vue", "Vue").Score.Should().Be(80, "a rule counts once");
    }

    [Fact]
    public void Shipped_configuration_is_valid() => ShippedConfig.Relevance().Validate().Should().BeEmpty();

    [Fact]
    public void Invalid_configuration_is_reported()
    {
        var options = new RelevanceOptions
        {
            RelevantFrom = 30,
            MaybeFrom = 40,
            Positive = { new RelevanceRule { Name = "", Terms = { " " }, Weight = 0 } },
        };

        options.Validate().Should().HaveCount(4);
    }
}
