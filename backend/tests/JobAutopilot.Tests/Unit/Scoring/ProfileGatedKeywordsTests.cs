using FluentAssertions;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;

namespace JobAutopilot.Tests.Unit.Scoring;

/// <summary>Keywords score only for skills I have; nice-to-have sections and basics count less (step 05d).</summary>
public class ProfileGatedKeywordsTests
{
    private static readonly CandidateProfile Profile = new()
    {
        Skills =
        {
            new Skill { Name = "C#", Level = SkillLevel.Production },
            new Skill { Name = "Azure", Level = SkillLevel.Learning },
            new Skill { Name = "Angular", Level = SkillLevel.None },
            new Skill { Name = "Git", Level = SkillLevel.Production },
            new Skill { Name = "Scrum", Level = SkillLevel.Production },
            new Skill { Name = "Code Reviews", Level = SkillLevel.Production },
            new Skill { Name = "Bootstrap", Level = SkillLevel.Production },
        },
    };

    private static RelevanceRule Keyword(string name, int weight) => new() { Name = name, Terms = { name }, Weight = weight };

    [Fact]
    public void A_keyword_counts_by_my_level_and_unknown_technologies_do_not_count()
    {
        var rules = ProfileRelevanceScorerProvider.ForProfile(
            [Keyword("C#", 25), Keyword("Azure", 4), Keyword("Angular", 10), Keyword("Blazor", 8), Keyword("Fullstack", 4)], Profile, 0.5).ToList();

        rules.Select(r => (r.Name, r.Weight)).Should().Equal(("C#", 25), ("Azure", 2), ("Fullstack", 4));
        ProfileRelevanceScorerProvider.ForProfile([Keyword("Blazor", 8)], null, 0.5).Should().ContainSingle("without a profile every keyword stays");
    }

    [Fact]
    public void Matches_only_in_a_nice_to_have_section_count_half()
    {
        var scorer = new RelevanceScorer(new RelevanceOptions { BaseScore = 0, Positive = { Keyword("Bootstrap", 8) } });

        scorer.Score("Developer", "Was du mitbringst\n- Bootstrap").Score.Should().Be(8);
        scorer.Score("Developer", "Dein Profil (Nice to Have)\n- Bootstrap").Reasons.Should().Contain("Bootstrap (nur „Plus“) ✓ (+4)");
    }

    [Fact]
    public void Basics_share_one_small_cap_and_never_raise_a_score()
    {
        var options = new RelevanceOptions
        {
            BaseScore = 0,
            GroupCaps = { ["basics"] = 6, ["frontend"] = 10 },
            BasicSkills = { "Git", "Scrum", "Code Reviews", "HTML/CSS" },
            Positive = { Keyword("Git", 4), Keyword("Scrum", 4), Keyword("Code Reviews", 4) },
        };
        options.Positive.Add(new RelevanceRule { Name = "Vue", Terms = { "Vue" }, Weight = 10, Group = "frontend" });
        options.Positive.Add(new RelevanceRule { Name = "HTML/CSS", Terms = { "CSS" }, Weight = 4, Group = "frontend" });
        var scorer = new RelevanceScorer(options);

        var result = scorer.Score("Developer", "Git, Scrum, Code Reviews");
        result.Score.Should().Be(6);
        result.Reasons.Should().Contain("Gruppe basics gedeckelt auf 6 (-6)");
        scorer.Score("Developer", "Vue und CSS").Score.Should().Be(10, "HTML/CSS still counts in its own, already full group");
    }
}
