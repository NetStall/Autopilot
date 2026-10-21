using FluentAssertions;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Unit.Scoring;

namespace JobAutopilot.Tests.Unit.Llm;

/// <summary>Alternatives („oder", „z. B.") are one requirement (08a), and the German guard keeps a level the text states.</summary>
public class ExtractionAlternativesTests
{
    [Theory]
    [InlineData("- Erfahrung mit relationalen Datenbanken (z.B. **MSSQL,** **PostgreSQL, MySql**)",
        new[] { "relationale Datenbanken", "MSSQL", "PostgreSQL", "MySql", "C#" }, new[] { "relationale Datenbanken oder MSSQL oder PostgreSQL oder MySql", "C#" })]
    [InlineData("Benutzeroberflächen mit **Angular oder** **Vue.js**", new[] { "Angular", "Vue.js" }, new[] { "Angular oder Vue.js" })]
    [InlineData("Frontend mit React/Vue", new[] { "React", "Vue" }, new[] { "React oder Vue" })]
    [InlineData("Kenntnisse in Docker bzw. Kubernetes", new[] { "Docker", "Kubernetes" }, new[] { "Docker oder Kubernetes" })]
    [InlineData("Frameworks wie Angular, React oder Vue", new[] { "Angular", "React", "Vue" }, new[] { "Angular oder React oder Vue" })]
    [InlineData("Technologien wie C#, .NET und Vue", new[] { "C#", ".NET", "Vue" }, new[] { "C#", ".NET", "Vue" })]
    [InlineData("Du bringst C#, .NET und SQL mit.", new[] { "C#", ".NET", "SQL" }, new[] { "C#", ".NET", "SQL" })]
    public void Items_the_text_joins_become_one_entry(string line, string[] items, string[] expected)
    {
        ExtractionAlternatives.Merge(items, [line]).Should().Equal(expected);
    }

    [Fact]
    public void AraCom_has_no_angular_or_database_gap_and_ranks_clearly_higher()
    {
        // Real qwen3:8b v3 answer (08a): before, „relationale Datenbanken" was a must-have gap and generic phrases
        // („Teamarbeit", „neue Technologien") cost points as a plus — rank 62.
        var (vacancy, _) = RealWorldFixtures.ScoredWithExtraction("aracom-dotnet-angular-vue", "v3");
        var reasons = string.Join(" | ", vacancy.RelevanceReasons);

        // The invented test profile has no Entity Framework, Tailwind or AI skill — those gaps stay (−23).
        vacancy.RankScore.Should().BeGreaterThanOrEqualTo(70, reasons);
        vacancy.RelevanceReasons.Should().NotContain(r => r.Contains("Angular", StringComparison.Ordinal) && r.StartsWith("Fit:", StringComparison.Ordinal), reasons);
        vacancy.RelevanceReasons.Where(r => r.StartsWith("Fit:", StringComparison.Ordinal))
            .Should().NotContain(r => r.Contains("relationale", StringComparison.Ordinal) || r.Contains("SQL Server", StringComparison.Ordinal)
                || r.Contains("MSSQL", StringComparison.Ordinal) || r.Contains("Teamarbeit", StringComparison.Ordinal), reasons);
    }

    [Fact]
    public void A_german_level_the_text_states_is_kept()
    {
        var extraction = new RequirementExtraction(
            ["C#"], [], LanguageLevel.C1, false, null, null, Seniority.Unknown, RemoteLevel.Unknown, null, EmploymentType.Unknown, [], "Ein Job.");

        ExtractionGuards.Apply(extraction, "Du sprichst sehr gut Deutsch.").GermanLevel.Should().Be(LanguageLevel.C1);
        ExtractionGuards.Apply(extraction, "We build software in Deutschland.").GermanLevel.Should().BeNull("only the country, not the language");
    }
}
