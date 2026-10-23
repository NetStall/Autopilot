using FluentAssertions;
using JobAutopilot.Core.Filters;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Tests.Unit.Scoring;

namespace JobAutopilot.Tests.Unit.Filters;

public class HardFiltersTests
{
    private static readonly HardFilterSettings Defaults = new();

    private static Vacancy Make(string title, string? description = "Wir entwickeln mit C# und Vue.", bool unreachable = false) =>
        new() { Id = Guid.NewGuid(), Title = title, Company = "Firma", Url = "https://example.org", ExternalId = "1", Description = description, Unreachable = unreachable };

    [Theory]
    [InlineData("Werkstudent Softwareentwicklung C# (m/w/d)", HardFilterReason.RoleType)]
    [InlineData("Praktikum .NET", HardFilterReason.RoleType)]
    [InlineData("Duales Studium Informatik", HardFilterReason.RoleType)]
    [InlineData("Head of Engineering", HardFilterReason.RoleType)]
    [InlineData("Lead Developer .NET", HardFilterReason.RoleType)]
    [InlineData("Java Developer (m/w/d)", HardFilterReason.Stack)]
    [InlineData("SAP ABAP Entwickler", HardFilterReason.Stack)]
    [InlineData("Embedded Software Engineer C++", HardFilterReason.Stack)]
    public void A_title_filter_names_its_reason(string title, HardFilterReason reason)
    {
        HardFilters.Check(Make(title), Defaults, hideUnreachable: true)!.Reason.Should().Be(reason);
    }

    [Theory]
    [InlineData("Java / C# Entwickler")]
    [InlineData("Fullstack Developer JavaScript")]
    [InlineData("Softwareentwickler (m/w/d)")]
    public void My_stack_in_the_title_or_a_neutral_title_stays(string title)
    {
        HardFilters.Check(Make(title), Defaults, hideUnreachable: true).Should().BeNull();
    }

    [Fact]
    public void No_core_skill_anywhere_is_filtered_but_only_with_a_description()
    {
        HardFilters.Check(Make("Softwareentwickler", "Wir arbeiten mit Go und Rust."), Defaults, true)!.Reason.Should().Be(HardFilterReason.Stack);
        HardFilters.Check(Make("Softwareentwickler", null), Defaults, true).Should().BeNull("without a description the stack is unknown");
    }

    [Theory]
    [InlineData("Verhandlungssichere Deutschkenntnisse sind Voraussetzung.", true)]
    [InlineData("Deutsch auf C1-Niveau", true)]
    [InlineData("Deutsch als Muttersprache", true)]
    [InlineData("Sehr gute Deutschkenntnisse", false)]
    [InlineData("Gute Deutschkenntnisse", false)]
    public void Only_explicit_c1_german_is_filtered(string sentence, bool filtered)
    {
        var result = HardFilters.Check(Make(".NET Entwickler", $"C# und Vue. {sentence}"), Defaults, true);

        (result?.Reason == HardFilterReason.GermanC1).Should().Be(filtered);
        HardFilters.Check(Make(".NET Entwickler", $"C# und Vue. {sentence}"), new HardFilterSettings { GermanC1 = false }, true).Should().BeNull("„C1-Stellen trotzdem zeigen“");
    }

    [Fact]
    public void Reachability_follows_the_existing_switch()
    {
        HardFilters.Check(Make(".NET Entwickler", unreachable: true), Defaults, hideUnreachable: true)!.Reason.Should().Be(HardFilterReason.Unreachable);
        HardFilters.Check(Make(".NET Entwickler", unreachable: true), Defaults, hideUnreachable: false).Should().BeNull();
    }

    [Theory]
    [InlineData("lindenhof-full")]
    [InlineData("vektor-dotnet")]
    [InlineData("elbsoft-csharp-angular")]
    [InlineData("foerderwerk-fullstack-vue")]
    [InlineData("brueckenwerk-dotnet-angular-vue")]
    public void None_of_my_passt_vacancies_is_filtered(string fixture)
    {
        // My gold labels „Passt" (08b safety rule); the list exempts them anyway — the rules must not catch them either.
        var vacancy = RealWorldFixtures.Scored(fixture);

        HardFilters.Check(vacancy, Defaults, hideUnreachable: true).Should().BeNull(string.Join(" | ", vacancy.RelevanceReasons));
    }
}
