using FluentAssertions;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit.Scoring;

/// <summary>
/// Real-world postings must keep their place in the ranking: a rule change that moves one of them out of its range
/// fails here instead of silently changing the list. Ranges, not exact numbers — small tuning is fine.
/// </summary>
public class RankingRegressionTests
{
    public static TheoryData<string, int, int> Ranges => new()
    {
        // .NET + „Angular oder Vue.js": my stack, Angular only as an alternative to Vue; Senior → small penalty.
        { "aracom-dotnet-angular-vue", 80, 95 },
        // Senior C#/.NET, but AWS is a must-have gap, German B2 („sicher auf Deutsch"), strong seniority signal (05c).
        { "medifox-senior-aws", 33, 41 },
        // C# but C1 German and hybrid outside my region („anteilig Homeoffice" beats the 100 % field).
        { "ifm-prover-csharp", 28, 42 },
        // Same company, other main language / framework: must stay at the bottom.
        { "aracom-java-angular-vue", 0, 10 },
        { "aracom-go-vue", 0, 10 },
        // React/Next.js junior job: nothing of my stack.
        { "nextjs-junior", 0, 15 },
    };

    [Theory]
    [MemberData(nameof(Ranges))]
    public void Real_postings_keep_their_rank_range(string fixture, int min, int max)
    {
        var vacancy = RealWorldFixtures.Scored(fixture);

        vacancy.RankScore.Should().BeInRange(min, max, "reasons: {0}", string.Join(" | ", vacancy.RelevanceReasons));
    }

    [Fact]
    public void Medifox_has_sensible_reasons_and_no_false_matches()
    {
        var vacancy = RealWorldFixtures.Scored("medifox-senior-aws");

        vacancy.RelevanceReasons.Should().Contain("Fit: Muss-Anforderung, nicht im Profil: AWS (−15)");
        vacancy.RelevanceReasons.Should().Contain(r => r.StartsWith("Fit: Deutsch B2 gefordert („sicher auf Deutsch“)", StringComparison.Ordinal));
        vacancy.RelevanceReasons.Should().Contain(r => r.Contains("deutliches Signal", StringComparison.Ordinal));
        vacancy.RelevanceReasons.Should().NotContain(r => r.StartsWith("Vitest", StringComparison.Ordinal), "„Unit Tests“ is no evidence for Vitest");
        vacancy.RelevanceReasons.Should().NotContain(r => r.StartsWith("GitHub Actions", StringComparison.Ordinal), "„CI/CD“ is no evidence for GitHub Actions");
        vacancy.EmployerType.Should().Be(EmployerType.Direct, "Himalayas lists the hiring company");
        vacancy.RequiredGerman.Should().Be(RequiredGerman.B2);
        vacancy.RemoteLevel.Should().Be(RemoteLevel.FullRemote);
    }

    [Fact]
    public void Medifox_requirements_are_classified_by_section()
    {
        var (vacancy, _) = RealWorldFixtures.Load("medifox-senior-aws");
        var fit = new Core.Fit.FitAnalyzer(RealWorldFixtures.Fit).Analyze(
            RealWorldFixtures.Profile,
            new Core.Fit.FitInput(vacancy.Title, vacancy.Description, EmployerType.Direct, vacancy.Location, RemoteLevel.FullRemote),
            RealWorldFixtures.Today,
            Core.Fit.FitSettings.From(RealWorldFixtures.SearchProfile));

        fit.Requirements.Single(r => r.Name == "AWS").Importance.Should().Be(Core.Fit.RequirementImportance.MustHave);
        fit.Requirements.Single(r => r.Name == "Docker").Importance.Should().Be(Core.Fit.RequirementImportance.NiceToHave);
        fit.Requirements.Should().NotContain(r => r.Name == "Vitest" || r.Name == "GitHub Actions");
    }
}
