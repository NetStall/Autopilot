using FluentAssertions;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Unit.Scoring;

namespace JobAutopilot.Tests.Unit.Llm;

/// <summary>fitscore.v3 (08c): the model gets the rules' match and may not undo it (Donau case).</summary>
public class FitScoreMatchTests
{
    private static readonly FitResult Fit = new(
        [
            new("C#", FitCoverage.Covered, false, "beruflich", RequirementImportance.MustHave),
            new("SQL Server", FitCoverage.Partial, false, "verwandt: SQL", RequirementImportance.MustHave),
            new("Sage", FitCoverage.Gap, true, "nicht im Profil", RequirementImportance.NiceToHave),
            new("WPF", FitCoverage.Gap, true, "nicht im Profil", RequirementImportance.NiceToHave),
            new("Angular", FitCoverage.Gap, false, "nicht im Profil", RequirementImportance.Alternative),
            new("Kubernetes", FitCoverage.Gap, false, "nicht im Profil", RequirementImportance.MustHave),
        ],
        [new FitWarning("Deutsch C1 gefordert", 16)], Core.Profile.LanguageLevel.C1, null, 16);

    [Fact]
    public void The_match_lists_fulfilled_partial_gaps_alternatives_german_and_employer()
    {
        var text = FitScoreMatch.Describe(Fit, EmployerType.Direct);

        text.Should().Contain("erfüllt: C# [Muss]")
            .And.Contain("SQL Server [Muss, verwandt: SQL]")
            .And.Contain("Lücke: Sage [Plus], WPF [Plus], Kubernetes [Muss]")
            .And.Contain("Alternativen (durch einen anderen Skill erfüllt, keine Lücke): Angular")
            .And.Contain("Deutsch gefordert: C1").And.Contain("Arbeitgeber: direkter Arbeitgeber").And.Contain("Erreichbar: ja");
    }

    [Fact]
    public void A_plus_or_a_partial_match_is_never_a_blocker_and_partial_is_never_no_experience()
    {
        var score = new AiFitScore(40, "eher nicht",
            ["Keine Erfahrung mit SQL Server.", "C# passt.", "Deutsch C1 ist hoch."],
            ["Keine Erfahrung mit SQL Server", "Sage idealerweise gefordert", "WPF fehlt", "Angular fehlt", "Kubernetes gefordert", "Reisetätigkeit 80 %"]);

        var checkedScore = FitScoreMatch.Check(score, Fit);

        checkedScore.Blockers.Should().Equal("Kubernetes gefordert", "Reisetätigkeit 80 %");
        checkedScore.Reasons[0].Should().Be("teilweise Erfahrung mit SQL Server.");
    }

    [Fact]
    public void Sql_server_found_only_through_my_sql_is_related_not_covered()
    {
        var extraction = new RequirementExtraction(["SQL Server"], [], null, false, null, null, Seniority.Unknown, RemoteLevel.Unknown, null, EmploymentType.Unknown, [], "Ein Job.");

        var resolved = ExtractionNormalizer.Resolve(extraction, RealWorldFixtures.Profile);

        resolved.Should().ContainSingle().Which.Should().Match<ResolvedRequirement>(r => r.Name == "SQL Server" && r.Related && r.Skill!.Name == "SQL");
    }
}
