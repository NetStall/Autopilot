using FluentAssertions;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.SearchProfiles;
using JobAutopilot.Core.Vacancies;
using Microsoft.Extensions.Configuration;

namespace JobAutopilot.Tests.Unit.Fit;

/// <summary>The fit check with the shipped Fit configuration and a profile like Vitalii's (B1, 3.5 years, Vue not React).</summary>
public class FitAnalyzerTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static readonly FitOptions Options = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
        .Build().GetSection(FitOptions.SectionName).Get<FitOptions>()!;

    private readonly FitAnalyzer _analyzer = new(Options);

    private static CandidateProfile Profile { get; } = new()
    {
        Personal = new PersonalInfo { Name = "Test", Email = "t@example.org" },
        Experience =
        {
            new ExperienceEntry { Company = "A", Title = "Dev", From = "2022-04", To = "2025-09" },
        },
        Skills =
        {
            Skill("C#", SkillLevel.Production, "CSharp"),
            Skill(".NET", SkillLevel.Production, "dotnet", ".NET Core", "ASP.NET Core"),
            Skill("Vue", SkillLevel.Production, "Vue.js", "Vue 3"),
            Skill("TypeScript", SkillLevel.Production),
            Skill("PostgreSQL", SkillLevel.Project, "Postgres"),
            Skill("Azure", SkillLevel.Learning),
            Skill("Angular", SkillLevel.None),
            Skill("React", SkillLevel.None, "React.js", "Next.js"),
        },
        KnownGaps =
        {
            new KnownGap { Skill = "Angular", De = "Mit Angular noch nicht gearbeitet, aber 3 Jahre Vue 3.", En = "Not Angular yet, but 3 years of Vue 3." },
            new KnownGap { Skill = "React", Aliases = { "Next.js" }, De = "React noch nicht, aber Vue 3 + TypeScript.", En = "No React yet, but Vue 3 + TypeScript." },
        },
        Languages = { new LanguageSkill { Name = "Deutsch", Level = LanguageLevel.B1 }, new LanguageSkill { Name = "Englisch", Level = LanguageLevel.B2 } },
    };

    [Theory]
    [InlineData("Sehr gute Deutschkenntnisse in Wort und Schrift.", LanguageLevel.C1)]
    [InlineData("Deutsch auf C1-Niveau", LanguageLevel.C1)]
    [InlineData("Verhandlungssichere Deutsch- und Englischkenntnisse", LanguageLevel.C1)]
    [InlineData("Du sprichst fließend Deutsch und Englisch.", LanguageLevel.C1)]
    [InlineData("Deutsch auf Muttersprachniveau", LanguageLevel.C1)]
    [InlineData("Fluent German and English required.", LanguageLevel.C1)]
    [InlineData("German (C2) is a must", LanguageLevel.C2)]
    [InlineData("Gute Deutschkenntnisse (mind. B2)", LanguageLevel.B2)]
    [InlineData("Good German skills", LanguageLevel.B2)]
    [InlineData("Deutsch B1 ist ausreichend", LanguageLevel.B1)]
    public void Detects_the_required_german_level(string snippet, LanguageLevel expected) =>
        FitAnalyzer.RequiredGerman(snippet)!.Value.Level.Should().Be(expected);

    [Theory]
    [InlineData("Sehr gute Englischkenntnisse")]
    [InlineData("Standort Deutschland, B2B-Kunden")]
    [InlineData("Wir sind ein deutsches Unternehmen mit C1-Kunden")]
    public void Does_not_mistake_other_text_for_a_german_requirement(string snippet) =>
        FitAnalyzer.RequiredGerman(snippet).Should().BeNull();

    [Fact]
    public void German_above_my_level_is_a_warning_with_a_penalty_per_level()
    {
        var result = Analyze("Softwareentwickler C#", "Sehr gute Deutschkenntnisse sind Voraussetzung.");

        result.RequiredGerman.Should().Be(LanguageLevel.C1);
        result.Warnings.Should().ContainSingle(w => w.Text.StartsWith("Deutsch C1 gefordert", StringComparison.Ordinal))
            .Which.Penalty.Should().Be(16, "C1 is two levels above B1");
    }

    [Theory]
    [InlineData("Softwareentwickler", "Mindestens 5 Jahre Berufserfahrung in der Softwareentwicklung.", 5)]
    [InlineData("Softwareentwickler", "Du bringst 3+ Jahre Erfahrung mit .NET mit.", 3)]
    [InlineData("Softwareentwickler", "Mehrjährige Erfahrung mit C# und SQL", 3)]
    [InlineData("Softwareentwickler", "Langjährige Berufserfahrung im Backend", 5)]
    [InlineData("Software Engineer", "At least 4 years of experience with C#.", 4)]
    [InlineData("Senior .NET Developer (m/w/d)", "Wir freuen uns auf dich.", 5)]
    public void Detects_the_expected_years(string title, string description, int expected) =>
        Analyze(title, description).RequiredYears.Should().Be(expected);

    [Fact]
    public void More_years_than_mine_is_a_warning_not_an_exclusion_and_fewer_is_fine()
    {
        Analyze("Entwickler", "Mindestens 5 Jahre Erfahrung.").Warnings
            .Should().ContainSingle(w => w.Text.Contains("5 Jahre erwartet, im Profil 3.5", StringComparison.Ordinal));
        Analyze("Entwickler", "Erste 2 Jahre Erfahrung mit C#.").Warnings.Should().BeEmpty();
        Analyze("Entwickler", "Wir bieten 30 Tage Urlaub und 2 Jahre Probezeit.").RequiredYears.Should().BeNull("no experience word");
    }

    [Fact]
    public void Classifies_requirements_by_profile_level_with_dotnet_aliases()
    {
        var result = Analyze("Fullstack Developer", "Backend mit ASP.NET Core, Frontend mit Vue.js, Datenbank Postgres, Cloud: Azure.");

        result.Requirements.Should().Contain(new FitRequirement(".NET", FitCoverage.Covered, false, "beruflich"));
        result.Requirements.Should().Contain(r => r.Name == "Vue" && r.Coverage == FitCoverage.Covered);
        result.Requirements.Should().Contain(r => r.Name == "PostgreSQL" && r.Coverage == FitCoverage.Partial);
        result.Requirements.Should().Contain(r => r.Name == "Azure" && r.Coverage == FitCoverage.Partial);
        result.Penalty.Should().Be(0);
    }

    [Fact]
    public void Angular_as_main_framework_is_a_gap_with_the_honest_phrasing_and_a_penalty()
    {
        var result = Analyze("Fullstack Entwickler .NET / Angular", "Du entwickelst Frontends mit Angular und TypeScript.");

        result.Requirements.Should().Contain(new FitRequirement("Angular", FitCoverage.Gap, false,
            "Mit Angular noch nicht gearbeitet, aber 3 Jahre Vue 3."));
        result.Warnings.Should().Contain(w => w.Text == "Gefordert, aber nicht im Profil: Angular" && w.Penalty == 6);
    }

    [Fact]
    public void Vue_or_react_keeps_vue_covered_and_react_an_optional_alternative()
    {
        var result = Analyze("Frontend Developer", "Vue.js oder React. Erfahrung mit React ist von Vorteil.");

        result.Requirements.Should().Contain(r => r.Name == "Vue" && r.Coverage == FitCoverage.Covered);
        var react = result.Requirements.Single(r => r.Name == "React");
        react.Coverage.Should().Be(FitCoverage.Gap);
        react.Optional.Should().BeTrue("offered as an alternative to Vue and otherwise only „von Vorteil\"");
        result.Penalty.Should().Be(0);
    }

    [Fact]
    public void React_as_the_only_frontend_framework_is_a_required_gap()
    {
        var result = Analyze("Frontend Developer", "Du entwickelst unsere Oberfläche mit React und TypeScript.");

        result.Requirements.Single(r => r.Name == "React").Optional.Should().BeFalse();
        result.Penalty.Should().Be(6);
    }

    [Fact]
    public void Angular_or_vue_in_the_title_does_not_count_angular_as_required()
    {
        var result = Analyze("Senior Fullstack-Entwickler (m/w/d) – .NET & Angular oder Vue.js", "Wir entwickeln Individualsoftware.");

        result.Requirements.Single(r => r.Name == "Angular").Optional.Should().BeTrue();
        result.Warnings.Should().NotContain(w => w.Text.StartsWith("Gefordert", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gap_only_mentioned_as_nice_to_have_costs_only_a_little()
    {
        var result = Analyze("Backend Developer C#", "C# und .NET. Kenntnisse in Angular sind von Vorteil.");

        var angular = result.Requirements.Single(r => r.Name == "Angular");
        angular.Optional.Should().BeTrue();
        angular.Importance.Should().Be(RequirementImportance.NiceToHave);
        result.Warnings.Should().ContainSingle(w => w.Text == "Ein Plus, nicht im Profil: Angular").Which.Penalty.Should().Be(4);
    }

    [Fact]
    public void Common_requirements_outside_the_profile_are_gaps_and_warning_only_ones_just_warn()
    {
        var result = Analyze("Entwickler", "Kubernetes und GraphQL im Einsatz. Führerschein Klasse B erforderlich.");

        result.Requirements.Where(r => r.Coverage == FitCoverage.Gap).Select(r => r.Name)
            .Should().BeEquivalentTo("GraphQL", "Kubernetes");
        result.Warnings.Should().Contain(w => w.Text == "Führerschein gefordert – bitte prüfen" && w.Penalty == 0);
    }

    [Fact]
    public void Gaps_are_capped_and_an_avoided_employer_type_costs_points()
    {
        var settings = FitSettings.Neutral with { RankedLowerEmployerTypes = [EmployerType.Temporary] };
        var result = _analyzer.Analyze(Profile, new FitInput("Dev", "Angular, React, GraphQL, Kubernetes, AWS, WPF", EmployerType.Temporary, null, RemoteLevel.Unknown), Today, settings);

        result.Warnings.Single(w => w.Text.StartsWith("Gefordert", StringComparison.Ordinal)).Penalty.Should().Be(18);
        result.Warnings.Should().Contain(w => w.Text == "Zeitarbeit – laut Suchprofil niedriger einordnen" && w.Penalty == 10);
        result.Penalty.Should().Be(28);
    }

    [Theory]
    [InlineData("24103 Kiel", RemoteLevel.Hybrid, "80 % Homeoffice", 15, false)]
    [InlineData("24103 Kiel", RemoteLevel.Hybrid, "Homeoffice bis zu 40 %", 35, true)]
    [InlineData("24103 Kiel", RemoteLevel.Hybrid, "Text", 25, false)]
    [InlineData("24103 Kiel", RemoteLevel.Unknown, "Text", 20, false)]
    [InlineData("24103 Kiel", RemoteLevel.OnSite, "Text", 35, true)]
    [InlineData("94469 Deggendorf", RemoteLevel.Hybrid, "Text", 0, false)]
    [InlineData("93049 Regensburg", RemoteLevel.OnSite, "Text", 0, false)]
    [InlineData("24103 Kiel", RemoteLevel.FullRemote, "Text", 0, false)]
    [InlineData(null, RemoteLevel.Hybrid, "Text", 0, false)]
    public void Jobs_outside_my_regions_cost_points_unless_fully_remote_and_are_unreachable_on_site_or_with_little_home_office(
        string? location, RemoteLevel remote, string description, int penalty, bool unreachable)
    {
        var settings = FitSettings.Neutral with { Places = ["Deggendorf", "Regensburg"], RemoteGermany = true };

        var result = _analyzer.Analyze(Profile, new FitInput("Dev", description, EmployerType.Direct, location, remote), Today, settings);

        result.Warnings.Where(w => w.Text.StartsWith("Außerhalb meiner Regionen", StringComparison.Ordinal) || w.Text.StartsWith("Praktisch nicht erreichbar", StringComparison.Ordinal))
            .Sum(w => w.Penalty).Should().Be(penalty);
        result.Unreachable.Should().Be(unreachable);
    }

    [Fact]
    public void The_sources_home_office_field_wins_over_the_text()
    {
        var settings = FitSettings.Neutral with { Places = ["Deggendorf"], RemoteGermany = true };

        var result = _analyzer.Analyze(
            Profile, new FitInput("Dev", "80 % Homeoffice", EmployerType.Direct, "57392 Schmallenberg", RemoteLevel.Hybrid, HomeOfficePercent: 40), Today, settings);

        result.Unreachable.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Text == "Praktisch nicht erreichbar: 57392 Schmallenberg, Homeoffice bis 40 %" && w.Penalty == 35);
    }

    [Theory]
    [InlineData(RemotePreference.Any, RemoteLevel.OnSite, false)]
    [InlineData(RemotePreference.AtLeastHybrid, RemoteLevel.OnSite, true)]
    [InlineData(RemotePreference.AtLeastHybrid, RemoteLevel.Hybrid, false)]
    [InlineData(RemotePreference.AtLeastHybrid, RemoteLevel.Unknown, false)]
    [InlineData(RemotePreference.FullRemoteOnly, RemoteLevel.Hybrid, true)]
    [InlineData(RemotePreference.FullRemoteOnly, RemoteLevel.FullRemote, false)]
    public void The_remote_preference_of_the_suchprofil_costs_points_when_missed(RemotePreference preference, RemoteLevel level, bool penalised)
    {
        var settings = FitSettings.Neutral with { RemotePreference = preference };

        var result = _analyzer.Analyze(Profile, new FitInput("Dev", "Text", EmployerType.Direct, null, level), Today, settings);

        (result.Penalty == settings.Penalties.RemotePreference).Should().Be(penalised);
    }

    [Fact]
    public void Penalties_come_from_the_suchprofil()
    {
        var settings = FitSettings.Neutral with { Penalties = new SearchPenalties { GermanPerLevel = 3 } };

        var result = _analyzer.Analyze(Profile, new FitInput("Dev", "Sehr gute Deutschkenntnisse", EmployerType.Direct, null, RemoteLevel.Unknown), Today, settings);

        result.Penalty.Should().Be(6, "C1 is two levels above B1, 3 points each");
    }

    [Fact]
    public void Sentences_do_not_split_inside_asp_net_or_abbreviations() =>
        FitAnalyzer.Sentences("Wir nutzen ASP.NET Core, z.B. für APIs. Angular ist von Vorteil.").Select(s => s.Text.Trim())
            .Should().Equal("Wir nutzen ASP.NET Core, z.B. für APIs", "Angular ist von Vorteil.");

    private FitResult Analyze(string title, string description) =>
        _analyzer.Analyze(Profile, title, description, EmployerType.Direct, Today);

    private static Skill Skill(string name, SkillLevel level, params string[] aliases)
    {
        var skill = new Skill { Name = name, Level = level };
        skill.Aliases.AddRange(aliases);
        return skill;
    }
}
