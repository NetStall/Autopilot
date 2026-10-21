using FluentAssertions;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Fit;
using JobAutopilot.Core.Llm;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Vacancies;
using JobAutopilot.Infrastructure.Llm;
using JobAutopilot.Tests.Fakes;
using JobAutopilot.Tests.Unit.Scoring;
using Microsoft.Extensions.Options;

namespace JobAutopilot.Tests.Unit.Llm;

/// <summary>Step 07a2: guards against over-reading, methods group, thinking off, model names with tags, Himalayas remote.</summary>
public class ExtractionQualityTests
{
    private static RequirementExtraction Base() => ExtractionValidator.Parse("""
        {"mustHave":[],"niceToHave":[],"germanLevel":"unknown","englishLevel":"unknown","minYears":null,"seniority":"unknown",
         "remoteLevel":"unknown","homeOfficePercent":null,"employmentType":"unknown","domainRequirements":[],"summary":"Zwei Sätze. Hier."}
        """).Result!;

    [Fact]
    public void An_item_in_both_lists_stays_a_must_have_only_when_the_text_requires_it()
    {
        const string text = "Das bringst du mit\n- Kubernetes ist zwingend\n\nWir bieten\n- OpenTelemetry im Team";
        var extraction = Base() with { MustHave = ["Kubernetes", "OpenTelemetry", "C#"], NiceToHave = ["Kubernetes", "OpenTelemetry"] };

        var guarded = ExtractionGuards.Apply(extraction, text);

        guarded.MustHave.Should().Equal("Kubernetes", "C#");
        guarded.NiceToHave.Should().Equal("OpenTelemetry");
    }

    [Theory]
    [InlineData("Wir entwickeln Software für die digitale Patientenversorgung im Healthcare-Bereich.", false)]
    [InlineData("Erfahrung im Healthcare-Umfeld ist erforderlich.", true)]
    [InlineData("You have hands-on experience in broadcast environments.", true)]
    public void A_domain_stays_only_when_experience_in_it_is_asked_for(string text, bool kept)
    {
        var guarded = ExtractionGuards.Apply(Base() with { DomainRequirements = [text.Contains("broadcast", StringComparison.Ordinal) ? "Broadcast/MAM" : "Healthcare"] }, text);

        guarded.DomainRequirements.Should().HaveCount(kept ? 1 : 0);
    }

    [Theory]
    [InlineData("Du verfügst über mehrjährige Erfahrung in der Softwareentwicklung.", null)]
    [InlineData("Senior Developer mit langjähriger Erfahrung.", null)]
    [InlineData("You bring at least 5 years of experience.", 5)]
    [InlineData("Mindestens drei Jahre Berufserfahrung.", 5)]
    [InlineData("3+ Jahre mit C#.", 5)]
    public void Years_stay_only_when_the_text_states_a_number(string text, int? expected)
    {
        ExtractionGuards.Apply(Base() with { MinYears = 5 }, text).MinYears.Should().Be(expected);
    }

    [Fact]
    public void Free_wording_maps_to_my_testing_and_ci_skills_and_generic_practices_are_dropped()
    {
        var profile = RealWorldFixtures.Profile;
        var extraction = Base() with
        {
            MustHave = ["automatisierte Qualitätssicherung", "TDD", "CI/CD-Praktiken", "Cloud- und DevOps-Praktiken", "Qualitätsbewusstsein"],
        };

        var resolved = ExtractionNormalizer.Resolve(extraction, profile).ToDictionary(r => r.Name);

        resolved.Should().ContainKey("Vitest").WhoseValue.Skill!.Level.Should().Be(SkillLevel.Production);
        resolved.Should().ContainKey("GitHub Actions");
        resolved.Keys.Should().NotContain(k => k.Contains("Praktiken", StringComparison.Ordinal) || k.Contains("bewusstsein", StringComparison.Ordinal));
        resolved.Should().NotContainKey("TDD", "TDD is covered by my testing skill");
    }

    [Fact]
    public void A_copied_bullet_point_yields_its_known_terms_and_no_sentence_gap()
    {
        var extraction = Base() with
        {
            MustHave = ["Kenntnisse in .NET Backend-Entwicklung, einschließlich Technologien wie Kafka (oder Equivalente), AWS, Docker, ECS/Kubernetes und PostgreSQL",
                        "ein Wachstumsgedanke zum kontinuierlichen Erforschen neuer AI-Technologien"],
        };

        var names = ExtractionNormalizer.Resolve(extraction, RealWorldFixtures.Profile).Select(r => r.Name).ToList();

        names.Should().Contain([".NET", "Kafka", "AWS", "Docker", "Kubernetes", "PostgreSQL"]);
        names.Should().NotContain(n => n.Length > 40, "a sentence is no requirement name");
    }

    [Fact]
    public void Missing_methods_cost_like_a_plus_and_share_a_small_cap_unless_marked_as_required()
    {
        var analyzer = new FitAnalyzer(new FitOptions());
        var profile = RealWorldFixtures.Profile;
        var extraction = Base() with { MustHave = ["DDD", "CQRS", "Event Sourcing", "BDD"] };

        var normal = analyzer.Analyze(profile, new FitInput("Dev", "Wir nutzen DDD, CQRS, Event Sourcing und BDD.", EmployerType.Direct, null, RemoteLevel.Unknown, Extraction: extraction),
            RealWorldFixtures.Today, FitSettings.Neutral);
        var required = analyzer.Analyze(profile, new FitInput("Dev", "CQRS ist zwingend erforderlich.", EmployerType.Direct, null, RemoteLevel.Unknown, Extraction: extraction with { MustHave = ["CQRS"] }),
            RealWorldFixtures.Today, FitSettings.Neutral);

        normal.Warnings.Should().ContainSingle(w => w.Text.StartsWith("Methoden, nicht im Profil:", StringComparison.Ordinal)).Which.Penalty.Should().Be(6);
        normal.Warnings.Should().NotContain(w => w.Text.StartsWith("Muss-Anforderung", StringComparison.Ordinal));
        required.Warnings.Should().Contain(w => w.Text == "Muss-Anforderung, nicht im Profil: CQRS" && w.Penalty == 15);
    }

    [Fact]
    public void With_an_extraction_years_come_only_from_a_stated_number_never_from_seniority()
    {
        var analyzer = new FitAnalyzer(new FitOptions());
        var senior = Base() with { Seniority = Seniority.Senior };

        var result = analyzer.Analyze(RealWorldFixtures.Profile, new FitInput("Senior Developer", "Text", EmployerType.Direct, null, RemoteLevel.Unknown, Extraction: senior),
            RealWorldFixtures.Today, FitSettings.Neutral);

        result.RequiredYears.Should().BeNull();
    }

    [Fact]
    public void Both_prompt_versions_are_embedded_and_v2_is_the_default()
    {
        ExtractionPrompt.Current.Version.Should().Be("extraction.v2");
        ExtractionPrompt.For("extraction.v1").System.Should().NotBe(ExtractionPrompt.For("extraction.v2").System);
        ExtractionPrompt.Current.System.Should().Contain("Each item appears in ONE list only");
        new VacancyExtractionOptions().PromptVersion.Should().Be("extraction.v2");
        new VacancyExtractionOptions().Thinking.Should().BeFalse();
        var act = () => ExtractionPrompt.For("extraction.v9");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("ollama:qwen3.5:9b", "ollama", "qwen3.5:9b")]
    [InlineData("ollama:qwen3:14b", "ollama", "qwen3:14b")]
    [InlineData("claude-sonnet-5-5", "claude", "claude-sonnet-5-5")]
    public void A_model_tag_with_colons_is_kept_whole(string entry, string provider, string model)
    {
        var router = new LlmRouter([new FakeLlmClient(provider)], Options.Create(new LlmOptions { Tasks = { ["Extraction"] = entry } }));

        router.Resolve("Extraction").Should().Be((provider, model));
        router.ModelFor("Extraction").Should().Be(model);
        router.IsConfigured("Extraction").Should().BeTrue();
    }

    [Theory]
    [InlineData("You can work from home, the office or in a hybrid setup.", RemoteLevel.FullRemote)]
    [InlineData("Homeoffice-Möglichkeiten und flexible Zeiten.", RemoteLevel.FullRemote)]
    [InlineData("Die Stelle ist 100 % vor Ort, kein Homeoffice.", RemoteLevel.Hybrid)]
    public void Himalayas_remote_for_a_country_is_full_remote_unless_the_text_says_on_site(string text, RemoteLevel expected)
    {
        var vacancy = new Vacancy
        {
            Id = Guid.NewGuid(), Source = VacancySource.Himalayas, ExternalId = "h1", Title = "Senior Software Engineer (.NET)", Company = "Apaleo",
            Location = "Remote (Deutschland)", Url = "https://example.org", Description = text,
        };

        RealWorldFixtures.Enricher().Apply(vacancy);

        vacancy.RemoteLevel.Should().Be(expected);
    }
}
