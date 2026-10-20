using FluentAssertions;
using JobAutopilot.Core.Documents;
using JobAutopilot.Core.Profile;

namespace JobAutopilot.Tests.Unit.Documents;

public class CvBuilderTests
{
    private static readonly string[] Focuses = ["fullstack", "backend", "ai"];

    [Theory]
    [InlineData("Full-Stack-Entwickler (m/w/d)", "C#, Vue.js und TypeScript", "fullstack")]
    [InlineData("Backend Developer (m/w/d)", "C#, ASP.NET Core, SQL und REST APIs für unsere Microservices.", "backend")]
    [InlineData("Softwareentwickler (m/w/d)", "Frontend mit Vue und TypeScript, Backend mit C# und .NET.", "fullstack")]
    [InlineData("AI Engineer", "Build LLM applications with GPT and Claude, RAG pipelines, Machine Learning.", "ai")]
    [InlineData("Entwickler KI-Anwendungen (m/w/d)", "Wir bauen KI-Lösungen mit LLMs und OpenAI.", "ai")]
    public void Chooses_the_focus_from_the_posting(string title, string description, string expected) =>
        CvBuilder.DetectFocus(title, description, Focuses).Should().Be(expected);

    [Fact]
    public void Falls_back_to_an_available_focus()
    {
        CvBuilder.DetectFocus("AI Engineer", "LLM GPT Claude RAG", ["fullstack", "backend"]).Should().Be("fullstack");
        CvBuilder.DetectFocus("Backend", "C# SQL", ["ai"]).Should().Be("ai");
        CvBuilder.DetectFocus("x", null, []).Should().BeEmpty();
    }

    [Fact]
    public void German_cv_uses_the_german_profile_texts()
    {
        var cv = CvBuilder.Build(DocumentTestData.Profile(), DocumentTestData.Title, DocumentTestData.Description, DocumentLanguage.De);

        cv.Focus.Should().Be("fullstack");
        cv.Summary.Should().Be("Full-Stack-Entwickler mit 4 Jahren Erfahrung in C#/.NET und Vue 3.");
        cv.Experience.Single().Title.Should().Be("Softwareentwickler");
        cv.Experience.Single().Period.Should().Be("04/2021 – 03/2025");
        cv.Experience.Single().Bullets.Select(b => b.Text).Should().Contain("Entwickelte REST-APIs mit ASP.NET Core für drei Client-Anwendungen.");
        cv.Education.Single().Should().BeEquivalentTo(new CvEducation("B.Sc. Informatik", "Beispiel-Hochschule", "10/2017 – 03/2021", ["Abschlussarbeit zur Barrierefreiheit im Web"]));
        cv.Labels.Experience.Should().Be("BERUFSERFAHRUNG");
        cv.Languages.Should().Equal(new CvLanguage("Deutsch", "B2 (telc B2, 2024)"), new CvLanguage("Englisch", "C1"));
        cv.UntranslatedTexts.Should().Be(0);
    }

    [Fact]
    public void English_cv_uses_the_base_texts()
    {
        var cv = CvBuilder.Build(DocumentTestData.Profile(), "Full Stack Developer", "We use C# and Vue.", DocumentLanguage.En);

        cv.Summary.Should().Be("Full-stack developer with 4 years of C#/.NET and Vue 3.");
        cv.Experience.Single().Title.Should().Be("Software Developer");
        cv.Labels.Skills.Should().Be("SKILLS");
        cv.RoleLine.Should().StartWith("Full-Stack Developer · ");
    }

    [Fact]
    public void Missing_german_texts_are_counted()
    {
        var example = DocumentTestData.Profile();
        var profile = new CandidateProfile
        {
            Personal = example.Personal,
            Summary = new() { ["fullstack"] = "English only." },
            Experience = [new ExperienceEntry { Company = "A", Title = "Developer", From = "2022-01", To = "2023-01", Bullets = [new Bullet { Text = "Did things." }] }],
        };

        CvBuilder.Build(profile, "Entwickler", "C#", DocumentLanguage.De).UntranslatedTexts.Should().Be(3);
    }

    [Fact]
    public void Bullets_are_ordered_by_overlap_with_the_posting()
    {
        var cv = CvBuilder.Build(DocumentTestData.Profile(), "Frontend-Entwickler Vue (m/w/d)", "Vue.js und TypeScript", DocumentLanguage.En);

        cv.Experience.Single().Bullets[0].Text.Should().Be("Moved the frontend from jQuery to Vue 3 with TypeScript.");
    }

    [Fact]
    public void Manual_choices_hide_show_and_order_bullets_and_groups()
    {
        var options = new CvOptions("backend", ["exp0.b1"], [], ["exp0.b2", "exp0.b0"], ["Frontend"], ["DevOps & Tools"]);

        var cv = CvBuilder.Build(DocumentTestData.Profile(), DocumentTestData.Title, DocumentTestData.Description, DocumentLanguage.De, options);

        cv.Focus.Should().Be("backend");
        cv.Summary.Should().Be("Backend-Entwickler: REST-APIs mit ASP.NET Core, SQL-Datenbanken, CI/CD.");
        cv.Experience.Single().Bullets.Select(b => (b.Id, b.Hidden)).Should().Equal(("exp0.b2", false), ("exp0.b0", false), ("exp0.b1", true));
        cv.SkillGroups[0].Name.Should().Be("DevOps & Tools");
        cv.SkillGroups.Single(g => g.Name == "Frontend").Hidden.Should().BeTrue();
    }

    [Fact]
    public void Older_entries_show_three_bullets_unless_shown_explicitly()
    {
        var example = DocumentTestData.Profile();
        var bullets = Enumerable.Range(0, 6).Select(i => new Bullet { Text = $"Older bullet {i}." }).ToList();
        var profile = new CandidateProfile
        {
            Personal = example.Personal,
            Summary = example.Summary,
            Experience = [example.Experience[0], new ExperienceEntry { Company = "B", Title = "Junior", From = "2019-01", To = "2021-03", Bullets = bullets }],
            Skills = example.Skills,
        };

        var cv = CvBuilder.Build(profile, "Entwickler", "C#", DocumentLanguage.En);
        cv.Experience[1].Visible.Should().HaveCount(CvBuilder.MaxBulletsOlder);
        cv.Experience[1].Bullets.Where(b => b.Hidden).Should().OnlyContain(b => b.AutoHidden);

        var shown = CvBuilder.Build(profile, "Entwickler", "C#", DocumentLanguage.En, CvOptions.Default with { ShownBullets = ["exp1.b5"] });
        shown.Experience[1].Bullets.Single(b => b.Id == "exp1.b5").Hidden.Should().BeFalse();
    }

    [Fact]
    public void Skill_groups_list_only_used_skills_under_their_cv_headings()
    {
        var cv = CvBuilder.Build(DocumentTestData.Profile(), DocumentTestData.Title, DocumentTestData.Description, DocumentLanguage.De);

        cv.SkillGroups.Select(g => g.Name).Should().BeEquivalentTo("Backend", "Frontend", "DevOps & Tools");
        cv.SkillGroups.SelectMany(g => g.Skills).Should().NotContain(["Docker", "Angular"]);
        cv.SkillGroups.Single(g => g.Name == "Backend").Skills.Should().StartWith(["C#", ".NET"]);
    }

    [Fact]
    public void Role_line_names_the_focus_and_the_strongest_matching_skills()
    {
        var profile = DocumentTestData.Profile();

        CvBuilder.Build(profile, DocumentTestData.Title, DocumentTestData.Description, DocumentLanguage.De).RoleLine
            .Should().Be("Full-Stack-Entwickler · C# / .NET · Vue · TypeScript");
        CvBuilder.Build(profile, "Backend Developer (m/w/d)", "C#, ASP.NET Core, SQL, REST", DocumentLanguage.De).RoleLine
            .Should().Be("Backend-Entwickler · C# / .NET");
    }
}
