using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobAutopilot.Api.Features.Evaluation;
using JobAutopilot.Core.Llm;
using JobAutopilot.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobAutopilot.Tests.Integration;

/// <summary>„eval-ranking" (07c): metrics over the labels, and a run of another model that only fills the caches.</summary>
public class EvaluationApiTests(PostgresFixture postgres) : IntegrationTestBase(postgres)
{
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
    public async Task Ranks_the_labelled_vacancies_per_variant_and_runs_another_model_without_changing_them()
    {
        // Rules put the misfit first, the KI score puts the fit first.
        var fits = await SeedAsync(rank: 60, rules: 50, ai: 90);
        var misfit = await SeedAsync(rank: 80, rules: 85, ai: 30);
        var unlabelled = await SeedAsync(rank: 99, rules: 99, ai: 99);
        await Client.PutAsJsonAsync($"/api/vacancies/{fits.Id}/label", new { fits = true }, CancellationToken);
        await Client.PutAsJsonAsync($"/api/vacancies/{misfit.Id}/label", new { fits = false }, CancellationToken);

        var report = await Client.GetFromJsonAsync<RankingEvaluationDto>("/api/evaluation/ranking?implicit=false", CancellationToken);

        report!.Explicit.Should().Be(2);
        report.Rows.Select(r => r.VacancyId).Should().BeEquivalentTo([fits.Id, misfit.Id], "unlabelled vacancies are not measured");
        var results = report.Results.ToDictionary(r => r.Variant);
        results["Nur Regeln"].PrecisionAt10.Should().Be(0.5);
        results["Nur Regeln"].NdcgAt20.Should().BeLessThan(1);
        results["Nur KI-Bewertung"].NdcgAt20.Should().Be(1);
        results.Keys.Should().Contain("Rang + 0.5 × KI");

        _model.Answer = r => r.Task == "FitScore"
            ? """{"score":88,"verdict":"passt","reasons":["a","b","c"],"blockers":[]}"""
            : ExtractionApiTests.ValidJson;
        var run = await Client.PostAsync("/api/evaluation/model-run?model=qwen3.5:9b&implicit=false", null, CancellationToken);
        run.StatusCode.Should().Be(HttpStatusCode.OK);
        (await run.Content.ReadFromJsonAsync<ModelRunDto>(CancellationToken)).Should().BeEquivalentTo(new { Vacancies = 2, Extracted = 2, Scored = 2 });
        _model.Requests.Should().HaveCount(4).And.OnlyContain(r => r.Model == "qwen3.5:9b");

        var withModel = await Client.GetFromJsonAsync<RankingEvaluationDto>("/api/evaluation/ranking?implicit=false&model=qwen3.5:9b", CancellationToken);
        withModel!.Results.Select(r => r.Variant).Should().Contain(["Kombiniert (qwen3.5:9b)", "Nur KI-Bewertung (qwen3.5:9b)"]);
        withModel.Rows.Should().OnlyContain(r => r.ModelAi == 88);
        withModel.Rows.Select(r => (r.VacancyId, r.Combined)).Should().BeEquivalentTo(
            [(fits.Id, (int?)60), (misfit.Id, (int?)80)], "the current rank stays the stored one, whatever the other model gives");

        // A second run is cached; the vacancies keep their own scores.
        await Client.PostAsync("/api/evaluation/model-run?model=qwen3.5:9b&implicit=false", null, CancellationToken);
        _model.Requests.Should().HaveCount(4);
        await using var db = Postgres.CreateDbContext();
        (await db.Vacancies.SingleAsync(v => v.Id == fits.Id, CancellationToken)).Should().BeEquivalentTo(new { AiScore = 90, RankScore = 60, ExtractionJson = (string?)null });
        _ = unlabelled;
    }

    [Theory]
    [InlineData("/api/evaluation/ranking?model=bad%20model")]
    [InlineData("/api/evaluation/ranking?model=;rm")]
    public async Task Rejects_a_model_name_that_is_not_one(string url)
    {
        (await Client.GetAsync(url, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Core.Vacancies.Vacancy> SeedAsync(int rank, int rules, int ai)
    {
        var vacancy = VacancySeed.Make();
        vacancy.Description = $"Wir suchen C#-Entwickler ({Guid.NewGuid():N}).";
        (vacancy.RelevanceScore, vacancy.RankScore, vacancy.RuleRankScore, vacancy.AiScore) = (rank, rank, rules, ai);
        await using var db = Postgres.CreateDbContext();
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync(CancellationToken);
        return vacancy;
    }
}
