using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.Extraction;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Llm;
using JobAutopilot.Core.Profile;
using JobAutopilot.Tests.Fakes;
using JobAutopilot.Tests.Unit.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobAutopilot.Tests.Integration;

/// <summary>„KI-Bewertung" (step 07b) with a scripted model: in the extraction queue, cached, shown next to the rank.</summary>
public class AiFitScoreApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
    private const string ScoreJson = """
        {"score":72,"verdict":"passt","reasons":["C# und Vue passen.","Deutsch B2 reicht knapp.","Region passt."],"blockers":[]}
        """;

    private readonly FakeLlmClient _model = new();

    protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Profile:Path"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "profiles", "regression-profile.json"),
        ["Llm:Tasks:Extraction"] = "ollama:qwen3:8b",
        ["Llm:Tasks:FitScore"] = "ollama:qwen3:8b",
        ["Llm:Extraction:AutoAfterImport"] = "false",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ILlmClient>();
        services.AddSingleton<ILlmClient>(_model);
    }

    [Fact]
    public async Task The_score_runs_after_the_extraction_is_cached_and_does_not_change_the_rank()
    {
        _model.Answer = r => r.Task == "FitScore" ? ScoreJson : ExtractionApiTests.ValidJson;
        var vacancy = VacancySeed.Make(title: "Softwareentwickler C# (m/w/d)");
        vacancy.Description = "Wir suchen C#-Entwickler mit Broadcast-Erfahrung.";
        vacancy.RankScore = 60;
        await using (var db = Postgres.CreateDbContext())
        {
            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync(CancellationToken);
        }

        Factory.Services.GetRequiredService<ExtractionCoordinator>().RequestBackfill(100);
        await WaitIdleAsync();

        _model.Requests.Select(r => r.Task).Should().Equal("Extraction", "FitScore");
        var fitRequest = _model.Requests[1];
        fitRequest.User.Should().Contain("<profile>").And.Contain("Skills beruflich:").And.Contain("<vacancy>");
        fitRequest.User.Should().NotContain("@", "no e-mail address goes to the model");

        var list = await Client.GetFromJsonAsync<JsonElement>("/api/vacancies?minScore=0", CancellationToken);
        list.GetProperty("items")[0].GetProperty("aiScore").GetInt32().Should().Be(72);
        var detail = await Client.GetFromJsonAsync<JsonElement>($"/api/vacancies/{vacancy.Id}", CancellationToken);
        detail.GetProperty("aiFitScore").GetProperty("verdict").GetString().Should().Be("passt");
        detail.GetProperty("aiFitScore").GetProperty("reasons").GetArrayLength().Should().Be(3);

        await using (var db = Postgres.CreateDbContext())
        {
            (await db.VacancyFitScores.SingleAsync(CancellationToken)).Status.Should().Be(ExtractionStatus.Succeeded);
        }

        // Asking again changes nothing: both are cached for this text, profile, model and prompt.
        Factory.Services.GetRequiredService<ExtractionCoordinator>().RequestBackfill(100);
        await WaitIdleAsync();
        _model.Requests.Should().HaveCount(2);
    }

    [Fact]
    public void The_profile_text_has_skills_gaps_and_places_but_no_contact_data()
    {
        var profile = RealWorldFixtures.Profile;

        var text = FitScoreProfile.Build(profile, RealWorldFixtures.SearchProfile, RealWorldFixtures.Today);

        text.Should().Contain("Skills beruflich:").And.Contain("C#").And.Contain("Erreichbare Orte:");
        text.Should().NotContain(profile.Personal.Email).And.NotContain(profile.Personal.Name);
        FitScoreProfile.HashOf(text).Should().HaveLength(16).And.Be(FitScoreProfile.HashOf(text));
    }

    [Theory]
    [InlineData("""{"score":72,"verdict":"passt","reasons":["a"],"blockers":[]}""", true)]
    [InlineData("""{"score":120,"verdict":"passt","reasons":["a"],"blockers":[]}""", false)]
    [InlineData("""{"score":50,"verdict":"vielleicht","reasons":["a"],"blockers":[]}""", false)]
    [InlineData("""{"score":50,"verdict":"eher nicht","reasons":[],"blockers":[]}""", false)]
    public void The_answer_is_validated(string json, bool valid)
    {
        AiFitScoreValidator.Parse(json).Result.Should().Match<AiFitScore?>(r => (r != null) == valid);
    }

    private async Task WaitIdleAsync()
    {
        var coordinator = Factory.Services.GetRequiredService<ExtractionCoordinator>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        await Task.Delay(50, CancellationToken);
        while (coordinator.Status(true, "x").State != ExtractionState.Idle)
        {
            DateTime.UtcNow.Should().BeBefore(deadline);
            await Task.Delay(20, CancellationToken);
        }
    }
}
