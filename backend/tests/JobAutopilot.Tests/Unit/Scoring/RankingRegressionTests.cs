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
        // C# but C1 German, and hybrid outside my region with an unknown share („anteilig Homeoffice"): visible with a
        // moderate penalty (−20), not hidden as unreachable (06a).
        { "ifm-prover-csharp", 22, 38 },
        // C#/.NET in the title, but DevExpress (Pflicht), Blazor, SignalR, gRPC, XPO in the stack, and the source's
        // field says 40 % home office in Schmallenberg: not reachable (05d).
        { "schmidt-blazor-devexpress", 0, 35 },
        // Broadcast/MAM domain, codecs, FFmpeg, MXF, Kubernetes/Helm required; C# only one of three languages (05d).
        { "vidispine-broadcast", 0, 30 },
        // Title only (no description fetched): my stack in my region.
        { "tgw-fullstack-vue", 80, 95 },
        { "meierhofer-dotnet-vue", 80, 95 },
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
    public void Schmidt_counts_no_skill_i_do_not_have_and_is_not_reachable()
    {
        var vacancy = RealWorldFixtures.Scored("schmidt-blazor-devexpress");

        vacancy.RelevanceReasons.Should().NotContain(r => r.StartsWith("Blazor", StringComparison.Ordinal), "Blazor is not in my profile");
        vacancy.RelevanceReasons.Should().Contain(r => r.StartsWith("Fit: Muss-Anforderung, nicht im Profil: DevExpress", StringComparison.Ordinal));
        vacancy.RelevanceReasons.Should().Contain(r => r.StartsWith("Fit: Im Tech-Stack, nicht im Profil:", StringComparison.Ordinal)
            && r.Contains("Blazor", StringComparison.Ordinal) && r.Contains("SignalR", StringComparison.Ordinal) && r.Contains("gRPC", StringComparison.Ordinal));
        vacancy.RelevanceReasons.Should().Contain(r => r.StartsWith("Fit: Praktisch nicht erreichbar", StringComparison.Ordinal) && r.Contains("bis 40 %", StringComparison.Ordinal));
        vacancy.Unreachable.Should().BeTrue();
    }

    [Fact]
    public void Vidispine_domain_requirements_are_must_have_gaps()
    {
        var vacancy = RealWorldFixtures.Scored("vidispine-broadcast");

        vacancy.RelevanceReasons.Should().Contain(r => r.StartsWith("Fit: Muss-Anforderung, nicht im Profil:", StringComparison.Ordinal)
            && r.Contains("Broadcast", StringComparison.Ordinal) && r.Contains("FFmpeg", StringComparison.Ordinal) && r.Contains("MXF", StringComparison.Ordinal));
        vacancy.Unreachable.Should().BeFalse("fully remote");
    }

    [Fact]
    public void Meierhofer_with_the_v2_extraction_ranks_where_my_label_says()
    {
        var (before, _) = RealWorldFixtures.ScoredWithExtraction("meierhofer-full", "v1");
        var (after, extraction) = RealWorldFixtures.ScoredWithExtraction("meierhofer-full", "v2");

        after.RankScore.Should().BeGreaterThanOrEqualTo(60, "gold label Passt; reasons: {0}", string.Join(" | ", after.RelevanceReasons));
        after.RankScore.Should().BeGreaterThan(before.RankScore!.Value);
        extraction.DomainRequirements.Should().BeEmpty("the product is for patient care; healthcare experience is not required");
        extraction.MinYears.Should().BeNull("„mehrjährige Erfahrung“ states no number");
        after.RelevanceReasons.Should().NotContain(r => r.Contains("Healthcare", StringComparison.Ordinal) || r.Contains("Praktiken", StringComparison.Ordinal));
        after.RelevanceReasons.Should().NotContain(r => r.Contains("Senior-Rolle", StringComparison.Ordinal));
        after.RelevanceReasons.Should().Contain(r => r.StartsWith("Fit: Methoden, nicht im Profil:", StringComparison.Ordinal) && r.Contains("CQRS", StringComparison.Ordinal));
        after.RemoteLevel.Should().Be(RemoteLevel.Hybrid);
    }

    [Fact]
    public void Meierhofer_with_the_real_v3_extraction_counts_german_once()
    {
        // The real qwen3:8b answer (07b) lists „Deutsch (C1)" as a must-have: before 07c that cost −15 as a skill gap on
        // top of the German penalty, and the rank fell from 79 to 58 although my label is „Passt".
        var (vacancy, _) = RealWorldFixtures.ScoredWithExtraction("meierhofer-full", "v3");

        vacancy.RankScore.Should().BeGreaterThanOrEqualTo(70, "gold label Passt; reasons: {0}", string.Join(" | ", vacancy.RelevanceReasons));
        vacancy.RelevanceReasons.Should().NotContain(r => r.StartsWith("Fit: Muss-Anforderung", StringComparison.Ordinal) && r.Contains("Deutsch", StringComparison.Ordinal));
        vacancy.RelevanceReasons.Should().ContainSingle(r => r.StartsWith("Fit: Deutsch C1 gefordert", StringComparison.Ordinal));
    }

    [Fact]
    public void Mca_gets_no_german_level_the_posting_never_mentions()
    {
        // The real v3 answer says „C1", but the posting has no word about German: no penalty for it (07c).
        var (vacancy, extraction) = RealWorldFixtures.ScoredWithExtraction("mca-group-dotnet", "v3");

        extraction.GermanLevel.Should().BeNull();
        vacancy.RelevanceReasons.Should().NotContain(r => r.Contains("Deutsch", StringComparison.Ordinal), "reasons: {0}", string.Join(" | ", vacancy.RelevanceReasons));
    }

    [Fact]
    public void Apaleo_states_five_years_and_ranks_below_the_rules()
    {
        var rules = RealWorldFixtures.Scored("apaleo-senior-dotnet");
        var (after, extraction) = RealWorldFixtures.ScoredWithExtraction("apaleo-senior-dotnet", "v2");

        extraction.MinYears.Should().Be(5);
        extraction.MustHave.Should().Contain("Kafka");
        extraction.NiceToHave.Should().NotContain("Kafka", "one list per item; „What You'll Bring“ is a requirements section");
        after.RankScore.Should().BeLessThan(rules.RankScore!.Value, "reasons: {0}", string.Join(" | ", after.RelevanceReasons));
        after.RemoteLevel.Should().Be(RemoteLevel.FullRemote, "Himalayas „Remote (Deutschland)“; the hybrid setup is only an offer");
        rules.RemoteLevel.Should().Be(RemoteLevel.FullRemote);
    }

    [Theory]
    [InlineData("dimmel-csharp-angular", "teilweise im Homeoffice")]
    [InlineData("monari-dotnet-backend", "hybrides Arbeiten im")]
    public void A_bare_home_office_flag_does_not_outrank_a_partial_text_cue(string fixture, string cue)
    {
        var vacancy = RealWorldFixtures.Scored(fixture);

        vacancy.Unreachable.Should().BeTrue("outside my places and only partly home office; reasons: {0}", string.Join(" | ", vacancy.RelevanceReasons));
        vacancy.RemoteNote.Should().Be("Homeoffice: teilweise (laut Text)");
        vacancy.RelevanceReasons.Should().Contain(r => r.Contains(cue, StringComparison.Ordinal));
    }

    [Fact]
    public void The_field_share_and_an_unknown_share_keep_their_rules()
    {
        var schmidt = RealWorldFixtures.Scored("schmidt-blazor-devexpress");
        var ifm = RealWorldFixtures.Scored("ifm-prover-csharp");

        schmidt.RemoteNote.Should().Be("Homeoffice: bis 40 % (Feld)");
        schmidt.Unreachable.Should().BeTrue();
        ifm.Unreachable.Should().BeFalse("„anteilig Homeoffice“ names no share: visible with −20");
        ifm.RelevanceReasons.Should().Contain(r => r.Contains("Homeoffice-Anteil unbekannt (−20)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_flag_with_100_percent_remote_in_the_text_is_full_remote()
    {
        var vacancy = new Vacancy
        {
            Id = Guid.NewGuid(), Source = VacancySource.Arbeitsagentur, ExternalId = "x", Title = "Softwareentwickler C#", Company = "Firma",
            Location = "24103 Kiel", Url = "https://example.org", Description = "Du arbeitest 100 % remote aus ganz Deutschland.",
        };

        RealWorldFixtures.Enricher().Apply(vacancy, RemoteLevel.Hybrid);

        vacancy.RemoteLevel.Should().Be(RemoteLevel.FullRemote);
        vacancy.Unreachable.Should().BeFalse();
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
