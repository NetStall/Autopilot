using System.Text.Json.Nodes;
using FluentAssertions;
using JobAutopilot.Core.Profile;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Infrastructure.Profile;
using JobAutopilot.Tests.Fakes;
using JobAutopilot.Tests.Unit.Scoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobAutopilot.Tests.Unit.Profile;

public sealed class ProfileTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 19);
    private readonly string _dir = Directory.CreateTempSubdirectory("profile-tests-").FullName;

    public static string ExampleJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "profile.example.json"));

    [Fact]
    public void The_committed_example_profile_is_valid()
    {
        var (profile, validation) = FileCandidateProfileSource.Parse(ExampleJson, Today);

        validation.Errors.Should().BeEmpty();
        profile!.Personal.Name.Should().Be("Alex Beispiel");
        profile.Skills.Should().Contain(s => s.Name == "Angular" && s.Level == SkillLevel.None);
        profile.Languages.Single(l => l.Name == "Deutsch").Level.Should().Be(LanguageLevel.B2);
        ProfileValidator.ExperienceMonths(profile, Today).Should().Be(48);
    }

    [Theory]
    [InlineData("experience[0].from", "2021-13", "experience[0].from muss YYYY-MM sein.")]
    [InlineData("experience[0].to", "2020-01", "experience[0]: from liegt nach to.")]
    [InlineData("personal.website", "javascript:alert(1)", "personal.website muss ein http(s)-Link sein.")]
    [InlineData("knownGaps[0].en", "", "knownGaps[0] (Angular): die ehrliche Formulierung fehlt auf Deutsch (de) oder Englisch (en).")]
    [InlineData("preferences.remote", "Sometimes", "preferences.remote muss Remote, Hybrid, OnSite oder Any sein.")]
    public void Broken_values_are_reported_clearly(string path, string value, string expectedError)
    {
        var (_, validation) = FileCandidateProfileSource.Parse(With(path, value), Today);

        validation.Errors.Should().Contain(expectedError);
    }

    [Fact]
    public void An_unknown_skill_level_names_the_place()
    {
        var (profile, validation) = FileCandidateProfileSource.Parse(With("skills[0].level", "Expert"), Today);

        profile.Should().BeNull();
        validation.Errors.Should().ContainSingle().Which.Should().StartWith("Ungültiges JSON bei $.skills[0].level");
    }

    [Fact]
    public void Unknown_fields_are_rejected_so_typos_do_not_get_lost()
    {
        var json = JsonNode.Parse(ExampleJson, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
        json["personal"]!["nmae"] = "x";

        FileCandidateProfileSource.Parse(json.ToJsonString(), Today).Validation.Errors.Should().ContainSingle()
            .Which.Should().Contain("nmae");
    }

    [Fact]
    public void Overlapping_jobs_are_only_a_warning()
    {
        var json = JsonNode.Parse(ExampleJson, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
        var experience = json["experience"]!.AsArray();
        experience.Add(JsonNode.Parse("""{ "company": "Nebenjob", "title": "Tutor", "from": "2024-01", "to": "2024-06" }"""));

        var (_, validation) = FileCandidateProfileSource.Parse(json.ToJsonString(), Today);

        validation.Errors.Should().BeEmpty();
        validation.Warnings.Should().Contain("experience[0] und experience[1] überschneiden sich.");
    }

    [Fact]
    public void A_missing_file_gives_a_clear_message_and_the_file_is_reread_when_it_changes()
    {
        var path = Path.Combine(_dir, "profile.json");
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
        var source = new FileCandidateProfileSource(path, clock, NullLogger<FileCandidateProfileSource>.Instance);

        source.Current.Status.Should().Be(ProfileStatus.Missing);
        source.Current.Errors.Single().Should().Contain("profile.json").And.Contain("profile.example.json");

        File.WriteAllText(path, ExampleJson);
        clock.Advance(FileCandidateProfileSource.CheckInterval);
        var loaded = source.Current;
        loaded.Status.Should().Be(ProfileStatus.Loaded);

        File.WriteAllText(path, "{ not json");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        clock.Advance(FileCandidateProfileSource.CheckInterval);
        source.Current.Status.Should().Be(ProfileStatus.Invalid);
        source.Current.Version.Should().BeGreaterThan(loaded.Version);
        source.Current.Usable.Should().BeNull();
    }

    [Fact]
    public void Profile_skills_become_positive_keywords_by_level_but_configured_rules_win()
    {
        var (profile, _) = FileCandidateProfileSource.Parse(ExampleJson, Today);

        var rules = ProfileRelevanceScorerProvider.RulesFrom(profile, new ProfileSkillWeights()).ToList();
        rules.Should().Contain(r => r.Name == "Docker" && r.Weight == 1, "Learning");
        rules.Should().NotContain(r => r.Name == "Angular", "None adds nothing");

        var scorer = new RelevanceScorer(ShippedConfig.Relevance(), rules);
        scorer.Score("Entwickler", "Docker im Einsatz").Reasons.Should().Contain("Docker ✓ (+1)");
        scorer.Score("Entwickler", "C# im Einsatz").Reasons.Should().Contain("C# ✓ (+25)", "the configured C# rule, not the profile's");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The example profile with one value replaced, addressed like <c>experience[0].from</c>.</summary>
    private static string With(string path, string value)
    {
        var root = JsonNode.Parse(ExampleJson, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
        var parts = path.Split('.');
        JsonNode node = root;
        foreach (var part in parts[..^1])
        {
            node = Step(node, part);
        }

        var last = parts[^1];
        node.AsObject()[last] = value;
        return root.ToJsonString();

        static JsonNode Step(JsonNode node, string part)
        {
            var bracket = part.IndexOf('[', StringComparison.Ordinal);
            if (bracket < 0)
            {
                return node[part]!;
            }

            var index = int.Parse(part[(bracket + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
            return node[part[..bracket]]![index]!;
        }
    }
}
