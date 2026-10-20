using FluentAssertions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit.Fit;

/// <summary>Sections, importance of gaps, seniority signals, the region rule and generic aliases (step 05c).</summary>
public class RequirementImportanceTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    private static readonly FitAnalyzer Analyzer = new(new FitOptions
    {
        CommonRequirements = [new FitRequirementRule { Name = "AWS" }, new FitRequirementRule { Name = "Lambda" }, new FitRequirementRule { Name = "SQL Server", Terms = ["MSSQL"] }],
        OptionalMarkers = ["von Vorteil", "wünschenswert", "kein Muss", "ein Plus"],
    });

    private static readonly CandidateProfile Profile = new()
    {
        Experience = [new ExperienceEntry { Company = "A", Title = "Dev", From = "2022-04", To = "2025-09" }],
        Skills =
        {
            new Skill { Name = "C#", Level = SkillLevel.Production },
            new Skill { Name = "MySQL", Level = SkillLevel.Production },
            new Skill { Name = "Vitest", Aliases = ["Unit Tests", "Unit-Tests"], Level = SkillLevel.Production },
            new Skill { Name = "GitHub Actions", Aliases = ["CI/CD", "CI"], Level = SkillLevel.Production },
            new Skill { Name = "Docker", Aliases = ["Container"], Level = SkillLevel.Project },
        },
        Languages = { new LanguageSkill { Name = "Deutsch", Level = LanguageLevel.B1 } },
    };

    private static FitResult Analyze(string title, string description, RemoteLevel remote = RemoteLevel.Unknown, string? location = null, FitSettings? settings = null) =>
        Analyzer.Analyze(Profile, new FitInput(title, description, EmployerType.Direct, location, remote), Today, settings ?? FitSettings.Neutral);

    private const string Sectioned = """
        Was du bewirkst
        - Du betreibst Services in AWS und Lambda.

        Was du mitbringst
        - Erfahrung mit C# und AWS

        Ein Plus, aber kein Muss
        - Erfahrung mit Lambda
        """;

    [Fact]
    public void A_gap_in_the_must_have_section_costs_more_than_a_nice_to_have_one()
    {
        var result = Analyze("Backend Developer", Sectioned);

        result.Requirements.Single(r => r.Name == "AWS").Importance.Should().Be(RequirementImportance.MustHave);
        result.Warnings.Should().Contain(new FitWarning("Muss-Anforderung, nicht im Profil: AWS", 15));
        // Lambda: in the tasks (no section → required) and in „Ein Plus" — the more important mention wins.
        result.Requirements.Single(r => r.Name == "Lambda").Importance.Should().Be(RequirementImportance.Required);
    }

    [Fact]
    public void Only_nice_to_have_mentions_cost_little()
    {
        var result = Analyze("Backend Developer", "Was du mitbringst\n- C#\n\nWünschenswert\n- Lambda");

        result.Requirements.Single(r => r.Name == "Lambda").Importance.Should().Be(RequirementImportance.NiceToHave);
        result.Warnings.Should().Contain(new FitWarning("Ein Plus, nicht im Profil: Lambda", 4));
    }

    [Fact]
    public void Markdown_headings_and_sub_headings_are_understood()
    {
        var result = Analyze("Developer", "## Deine Aufgaben\n- Entwicklung\n## Das bringst du mit\n### **Backend**\n- AWS-Erfahrung\n## Das erwartet dich\n- Lambda im Team");

        result.Requirements.Single(r => r.Name == "AWS").Importance.Should().Be(RequirementImportance.MustHave, "„### Backend“ does not end the section");
        result.Requirements.Single(r => r.Name == "Lambda").Importance.Should().Be(RequirementImportance.Required, "„Das erwartet dich“ ends it");
    }

    [Fact]
    public void An_inline_must_have_marker_counts()
    {
        Analyze("Developer", "Sehr gute Kenntnisse in AWS (Must-have)").Requirements.Single(r => r.Name == "AWS")
            .Importance.Should().Be(RequirementImportance.MustHave);
    }

    [Fact]
    public void An_example_list_with_a_skill_i_have_is_no_gap_penalty()
    {
        var result = Analyze("Developer", "Was du mitbringst\n- Erfahrung mit relationalen Datenbanken (z.B. MSSQL, PostgreSQL, MySql)");

        result.Requirements.Single(r => r.Name == "SQL Server").Importance.Should().Be(RequirementImportance.Alternative);
        result.Penalty.Should().Be(0);
    }

    [Fact]
    public void All_gaps_share_one_cap()
    {
        var result = Analyze("Developer", "Was du mitbringst\n- AWS\n- Lambda\n- MSSQL");

        result.Warnings.Should().ContainSingle().Which.Should().Be(new FitWarning("Muss-Anforderung, nicht im Profil: AWS, Lambda, SQL Server", 18));
    }

    [Fact]
    public void Generic_aliases_are_no_evidence_for_a_specific_skill()
    {
        var result = Analyze("Developer", "Du baust CI/CD Pipelines auf und schreibst Unit Tests. Container-Plattform.");

        result.Requirements.Should().NotContain(r => r.Name == "Vitest" || r.Name == "GitHub Actions" || r.Name == "Docker");
    }

    [Fact]
    public void The_scorer_does_not_count_generic_aliases_either_and_shows_what_matched()
    {
        var scorer = ProfileRelevanceScorerProvider.Build(new RelevanceOptions { ProfileSkills = new ProfileSkillWeights { Production = 4, Project = 2, Learning = 1 } }, null, Profile);

        var generic = scorer.Score("Developer", "CI/CD Pipelines, Unit Tests und Container.");
        generic.Reasons.Should().NotContain(r => r.StartsWith("Vitest", StringComparison.Ordinal) || r.StartsWith("GitHub Actions", StringComparison.Ordinal) || r.StartsWith("Docker", StringComparison.Ordinal));

        scorer.Score("Developer", "Wir testen mit Vitest und deployen mit GitHub Actions.").Reasons
            .Should().Contain("Vitest ✓ (+4)").And.Contain("GitHub Actions ✓ (+4)");
        scorer.Score("Developer", "Unser Stack: C#, mysql.").Reasons.Should().Contain("MySQL ✓ (+4)", "case alone is not shown");
    }

    [Fact]
    public void The_profile_warns_about_generic_aliases()
    {
        var warnings = ProfileValidator.Validate(new CandidateProfile
        {
            Personal = new PersonalInfo { Name = "A", Email = "a@example.org" },
            Experience = [new ExperienceEntry { Company = "A", Title = "Dev", From = "2022-04", To = "2025-09" }],
            Skills = Profile.Skills,
        }, Today).Warnings;

        warnings.Should().Contain("skills[2] (Vitest): „Unit Tests“, „Unit-Tests“ sind allgemeine Begriffe und zählen nicht als Nachweis für Vitest.");
        warnings.Should().Contain("skills[4] (Docker): „Container“ ist ein allgemeiner Begriff und zählt nicht als Nachweis für Docker.");
    }

    [Theory]
    [InlineData("Senior Developer (m/w/d)", "Wir freuen uns auf dich.", 5, false)]
    [InlineData("Developer", "Langjährige Erfahrung in der Softwareentwicklung", 5, true)]
    [InlineData("Developer", "Du bringst 5+ Jahre Erfahrung mit .NET mit.", 5, true)]
    [InlineData("Developer", "Verfügst über mehrjährige Erfahrung in der Senior Softwareentwicklung mit C#", 5, true)]
    [InlineData("Lead Developer .NET", null, 5, true)]
    [InlineData("Teamleiter Softwareentwicklung", null, 5, true)]
    [InlineData("Developer", "Mehrjährige Erfahrung mit C# und SQL", 3, false)]
    [InlineData("Developer", "3+ Jahre Erfahrung", 3, false)]
    public void Seniority_signals_are_weak_or_strong(string title, string? description, int years, bool strong)
    {
        var text = description is null ? title : title + "\n" + description;

        var result = FitAnalyzer.RequiredYears(title, FitAnalyzer.Sentences(text));

        result.Should().NotBeNull();
        (result!.Years, result.Strong).Should().Be((years, strong));
    }

    [Fact]
    public void A_strong_seniority_signal_costs_more()
    {
        var weak = Analyze("Senior Developer", "Du entwickelst mit C#.");
        var strong = Analyze("Senior Developer", "Langjährige Erfahrung mit C#.");

        weak.Warnings.Should().ContainSingle(w => w.Text.Contains("5 Jahre erwartet", StringComparison.Ordinal)).Which.Penalty.Should().Be(8);
        strong.Warnings.Should().ContainSingle(w => w.Text.Contains("deutliches Signal", StringComparison.Ordinal)).Which.Penalty.Should().Be(10);
    }

    [Fact]
    public void A_fully_remote_job_based_elsewhere_gets_no_region_penalty()
    {
        var settings = new FitSettings(new SearchPenalties(), ["Deggendorf", "Regensburg"], RemoteGermany: true, RemotePreference.Any, []);

        var remote = Analyze("Developer", "C#. 100 % remote.", RemoteLevel.FullRemote, "20095 Hamburg", settings);
        var hybrid = Analyze("Developer", "C#. 2 Tage Homeoffice.", RemoteLevel.Hybrid, "20095 Hamburg", settings);

        remote.Warnings.Should().NotContain(w => w.Text.StartsWith("Außerhalb", StringComparison.Ordinal));
        hybrid.Warnings.Should().ContainSingle(w => w.Text.StartsWith("Außerhalb", StringComparison.Ordinal)).Which.Penalty.Should().Be(15);
    }

    [Fact]
    public void Section_headings_are_short_lines_without_bullet()
    {
        RequirementSections.HeadingText("## **Das bringst du mit**").Should().Be("Das bringst du mit");
        RequirementSections.HeadingText("Ein Plus, aber kein Muss").Should().Be("Ein Plus, aber kein Muss");
        RequirementSections.HeadingText("- Kenntnisse in Tailwind sind von Vorteil").Should().BeNull();
        RequirementSections.HeadingText("Wir suchen Verstärkung für unser Team und freuen uns auf deine Bewerbung als Entwickler.").Should().BeNull();
    }
}
