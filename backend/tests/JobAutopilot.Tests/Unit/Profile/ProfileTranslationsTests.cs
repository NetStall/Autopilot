using System.Text.Json;
using FluentAssertions;
using JobAutopilot.Core.Profile;

namespace JobAutopilot.Tests.Unit.Profile;

public class ProfileTranslationsTests
{
    private const string Json = """
        {
          // my comment stays
          "summary": { "fullstack": "Full-stack developer.", "backend": "Backend developer.", "backend.de": "Backend-Entwickler." },
          "experience": [
            {
              "company": "F&L",
              "title": "Team Lead",
              "bullets": [
                { "text": "Built REST APIs.", "tags": ["rest"] },
                { "text": "Moved to Vue 3.", "textDe": "Migrierte auf Vue 3." }
              ]
            }
          ],
          "education": [ { "institution": "Uni", "degree": "Bachelor of Economics" } ],
        }
        """;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    [Fact]
    public void Lists_every_text_without_its_german_field()
    {
        var profile = JsonSerializer.Deserialize<CandidateProfile>(Json, Options)!;

        ProfileTranslations.Missing(profile).Select(m => (m.Path, m.Target)).Should().Equal(
            ("summary.fullstack", "fullstack.de"),
            ("experience[0].title", "titleDe"),
            ("experience[0].bullets[0].text", "textDe"),
            ("education[0].degree", "degreeDe"));
    }

    [Fact]
    public void Inserts_approved_texts_next_to_their_source_and_keeps_the_rest_of_the_file()
    {
        var profile = JsonSerializer.Deserialize<CandidateProfile>(Json, Options)!;
        var missing = ProfileTranslations.Missing(profile);

        var updated = ProfileTranslations.Insert(Json, missing,
        [
            new("experience[0].bullets[0].text", "Entwickelte REST-APIs „für Kunden“."),
            new("summary.fullstack", "Full-Stack-Entwickler."),
            new("experience[0].bullets[1].text", "not missing — ignored"),
        ]);

        updated.Should().Contain("// my comment stays").And.Contain("\"text\": \"Built REST APIs.\", \"textDe\": \"Entwickelte REST-APIs „für Kunden“.\"");
        updated.Should().NotContain("ignored");
        var reread = JsonSerializer.Deserialize<CandidateProfile>(updated, Options)!;
        reread.Summary["fullstack.de"].Should().Be("Full-Stack-Entwickler.");
        reread.Experience[0].TitleDe.Should().BeNull("not approved");
        ProfileTranslations.Missing(reread).Should().HaveCount(2);
    }
}
