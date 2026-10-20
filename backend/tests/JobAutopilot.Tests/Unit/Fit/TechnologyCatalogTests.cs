using FluentAssertions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Core.Vacancies;

namespace JobAutopilot.Tests.Unit.Fit;

/// <summary>The technology catalog, line markers, stack sections and the home-office share (step 05d).</summary>
public class TechnologyCatalogTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    private static readonly FitAnalyzer Analyzer = new(new FitOptions { OptionalMarkers = ["von Vorteil", "nice to have", "kein Muss"] });

    private static readonly CandidateProfile Profile = new()
    {
        Experience = [new ExperienceEntry { Company = "A", Title = "Dev", From = "2020-01", To = "2025-09" }],
        Skills =
        {
            new Skill { Name = "C#", Level = SkillLevel.Production },
            new Skill { Name = ".NET", Level = SkillLevel.Production },
            new Skill { Name = "PostgreSQL", Level = SkillLevel.Project },
            new Skill { Name = "Kubernetes", Aliases = ["K8s"], Level = SkillLevel.None },
        },
        Languages = { new LanguageSkill { Name = "Deutsch", Level = LanguageLevel.C1 } },
    };

    private static FitResult Analyze(string description) =>
        Analyzer.Analyze(Profile, new FitInput("Developer", description, EmployerType.Direct, null, RemoteLevel.Unknown), Today, FitSettings.Neutral);

    [Fact]
    public void The_catalog_is_embedded_has_unique_names_and_no_generic_terms()
    {
        TechnologyCatalog.All.Should().HaveCountGreaterThanOrEqualTo(150);
        TechnologyCatalog.All.Select(t => SkillTerms.Key(t.Name)).Should().OnlyHaveUniqueItems();
        TechnologyCatalog.All.SelectMany(t => t.AllNames()).Should().NotContain(t => SkillTerms.IsGeneric(t));
        TechnologyCatalog.All.Where(t => t.Parent is not null).Select(t => t.Parent).Should().OnlyContain(p => TechnologyCatalog.Contains(p!));
        TechnologyCatalog.Contains("Blazor").Should().BeTrue();
        TechnologyCatalog.Contains("Fullstack").Should().BeFalse();
    }

    [Fact]
    public void Technologies_not_in_the_profile_are_gaps_by_section_and_marker()
    {
        var result = Analyze("""
            Sprachen & Technologien
            - C#/.NET – Mindestens 3 Jahre
            - Kommunikation: SignalR, gRPC
            - DevExpress (Pflicht)

            Dein Profil (Nice to Have)
            - Erfahrung mit Docker oder Terraform
            """);

        Importance(result, "DevExpress").Should().Be(RequirementImportance.MustHave, "„(Pflicht)\" wins over the section");
        Importance(result, "SignalR").Should().Be(RequirementImportance.LikelyRequired);
        Importance(result, "gRPC").Should().Be(RequirementImportance.LikelyRequired);
        Importance(result, "Terraform").Should().Be(RequirementImportance.NiceToHave);
        result.Requirements.Should().NotContain(r => r.Name == "C#" && r.Coverage == FitCoverage.Gap);
        result.Warnings.Should().Contain(new FitWarning("Muss-Anforderung, nicht im Profil: DevExpress", 15));
        result.Warnings.Should().Contain(new FitWarning("Im Tech-Stack, nicht im Profil: SignalR, gRPC", 18));
    }

    [Fact]
    public void Technologies_only_in_tasks_or_offer_and_parts_of_a_counted_gap_are_no_gaps()
    {
        var result = Analyze("""
            Deine Aufgaben
            - Migration unserer Services nach Kafka

            Anforderungen
            - Erfahrung mit AWS, insbesondere AWS Lambda und ECS
            - Kubernetes
            """);

        result.Requirements.Should().NotContain(r => r.Name == "Kafka");
        result.Requirements.Should().ContainSingle(r => r.Name == "AWS");
        result.Requirements.Should().NotContain(r => r.Name == "AWS Lambda" || r.Name == "Amazon ECS", "parts of AWS");
        result.Requirements.Should().ContainSingle(r => r.Name == "Kubernetes", "the profile's own entry, not a second one from the catalog");
    }

    [Fact]
    public void The_catalog_can_be_switched_off()
    {
        var plain = new FitAnalyzer(new FitOptions { UseTechnologyCatalog = false })
            .Analyze(Profile, new FitInput("Developer", "Anforderungen\n- DevExpress", EmployerType.Direct, null, RemoteLevel.Unknown), Today, FitSettings.Neutral);

        plain.Requirements.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Homeoffice bis zu 40 %", 40)]
    [InlineData("Homeoffice: bis zu 40%", 40)]
    [InlineData("60 % remote möglich", 60)]
    [InlineData("80 Prozent mobiles Arbeiten", 80)]
    [InlineData("2 Tage Homeoffice pro Woche", 40)]
    [InlineData("zwei Tage pro Woche im Homeoffice", 40)]
    [InlineData("Homeoffice an 3 Tagen", 60)]
    [InlineData("remote work 4 days a week", 80)]
    [InlineData("Homeoffice nach Absprache", null)]
    [InlineData("30 % Reisetätigkeit, sonst Büro", null)]
    [InlineData("", null)]
    public void The_home_office_share_is_read_from_percent_and_days(string text, int? expected)
    {
        HomeOfficeShare.Find(text).Should().Be(expected);
    }

    private static RequirementImportance Importance(FitResult result, string name) =>
        result.Requirements.Single(r => r.Name == name).Importance;
}
