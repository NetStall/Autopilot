using FluentAssertions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit.Fit;

public class GermanRequirementTests
{
    [Theory]
    // C1 or higher
    [InlineData("Verhandlungssichere Deutschkenntnisse", RequiredGerman.C1Plus)]
    [InlineData("Du sprichst Deutsch verhandlungssicher.", RequiredGerman.C1Plus)]
    [InlineData("Sehr gute Deutschkenntnisse in Wort und Schrift", RequiredGerman.C1Plus)]
    [InlineData("Du sprichst fließend Deutsch und Englisch.", RequiredGerman.C1Plus)]
    [InlineData("Deutsch fließend in Wort und Schrift", RequiredGerman.C1Plus)]
    [InlineData("Muttersprache Deutsch oder vergleichbar", RequiredGerman.C1Plus)]
    [InlineData("Deutsch als Muttersprache", RequiredGerman.C1Plus)]
    [InlineData("Deutschkenntnisse (Sprachniveau C1)", RequiredGerman.C1Plus)]
    [InlineData("Deutsch C2", RequiredGerman.C1Plus)]
    [InlineData("Fluent German and English required.", RequiredGerman.C1Plus)]
    [InlineData("German (C1) is a must", RequiredGerman.C1Plus)]
    // B2
    [InlineData("Gute Deutschkenntnisse (mind. B2)", RequiredGerman.B2)]
    [InlineData("Kommunizierst sicher auf Deutsch und Englisch", RequiredGerman.B2)]
    [InlineData("Sicheres Deutsch in Wort und Schrift", RequiredGerman.B2)]
    [InlineData("Deutsch auf B2-Niveau", RequiredGerman.B2)]
    [InlineData("Good German skills", RequiredGerman.B2)]
    // B1 or lower
    [InlineData("Deutsch B1 ist ausreichend", RequiredGerman.B1OrLower)]
    [InlineData("Deutschkenntnisse A2", RequiredGerman.B1OrLower)]
    [InlineData("Grundkenntnisse in Deutsch", RequiredGerman.B1OrLower)]
    [InlineData("Deutschkenntnisse sind von Vorteil", RequiredGerman.B1OrLower)]
    [InlineData("Deutschkenntnisse wünschenswert", RequiredGerman.B1OrLower)]
    [InlineData("Basic German is a plus.", RequiredGerman.B1OrLower)]
    // Mixed: the strongest finding for German wins, other languages do not count
    [InlineData("Deutsch C1, Englisch B2", RequiredGerman.C1Plus)]
    [InlineData("Englisch C1, Deutsch B2", RequiredGerman.B2)]
    [InlineData("Englisch verhandlungssicher, Deutsch B1", RequiredGerman.B1OrLower)]
    [InlineData("Gute Deutschkenntnisse, idealerweise verhandlungssicher", RequiredGerman.C1Plus)]
    public void Detects_the_required_level(string snippet, RequiredGerman expected) =>
        GermanRequirementDetector.Detect("Softwareentwickler (m/w/d)", snippet).Category.Should().Be(expected);

    [Theory]
    [InlineData("German is not required for this role. You will work with C# and Vue in our team.")]
    [InlineData("No German needed – English is our working language and we are a remote team.")]
    [InlineData("English is our company language. You will build APIs with .NET and our team works remote.")]
    public void Explicitly_not_required(string description) =>
        GermanRequirementDetector.Detect("Backend Developer", description).Category.Should().Be(RequiredGerman.None);

    [Fact]
    public void An_english_posting_without_any_german_requirement_needs_none()
    {
        var result = GermanRequirementDetector.Detect(
            "Senior Backend Engineer", "We are looking for an engineer to join our team. You will work with C#, .NET and PostgreSQL in a remote setup.");

        result.Category.Should().Be(RequiredGerman.None);
        result.Reason.Should().Be("Deutsch: nicht gefordert (englische Anzeige)");
    }

    [Fact]
    public void An_english_posting_asking_for_german_is_not_none()
    {
        GermanRequirementDetector.Detect("Backend Engineer", "You will work with C# in our team. Fluent German is required.")
            .Category.Should().Be(RequiredGerman.C1Plus);
    }

    [Fact]
    public void A_german_posting_without_a_phrase_is_unknown()
    {
        var result = GermanRequirementDetector.Detect("Softwareentwickler (m/w/d)", "Wir suchen dich für unser Team in Regensburg. Du entwickelst mit C#.");

        result.Should().Be(GermanRequirement.Unknown);
        result.Reason.Should().BeNull();
        GermanRequirementDetector.Detect("Softwareentwickler", null).Category.Should().Be(RequiredGerman.Unknown);
    }

    [Fact]
    public void The_reason_names_the_matched_phrase()
    {
        GermanRequirementDetector.Detect("Dev", "Kommunizierst sicher auf Deutsch und Englisch").Reason
            .Should().Be("Deutsch: B2 („sicher auf Deutsch“)");
        GermanRequirementDetector.Detect("Dev", "Deutschkenntnisse (Sprachniveau C1)").Reason
            .Should().Be("Deutsch: C1 oder höher („Deutschkenntnisse (Sprachniveau C1)“)");
    }
}
