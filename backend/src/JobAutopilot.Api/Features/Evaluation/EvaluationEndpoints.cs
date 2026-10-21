using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using JobAutopilot.Api.Features.Extraction;
using JobAutopilot.Core.Evaluation;
using JobAutopilot.Core.Extraction;
using JobAutopilot.Core.Scoring;
using JobAutopilot.Infrastructure.Llm;
using JobAutopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobAutopilot.Api.Features.Evaluation;

/// <summary>One labelled vacancy with the scores of every variant (ids and scores only — titles stay in the UI).</summary>
public sealed record EvaluationRowDto(
    Guid VacancyId, bool Fits, bool Automatic, int? Rules, int? Combined, int? Ai, int? ModelCombined, int? ModelAi, IReadOnlyList<string> AiBlockers);

/// <summary>Result of <c>GET /api/evaluation/ranking</c>.</summary>
public sealed record RankingEvaluationDto(
    bool WithAutomatic, int Explicit, int Automatic, int Target, string? Model, IReadOnlyList<RankingResult> Results, IReadOnlyList<EvaluationRowDto> Rows);

/// <summary>Result of <c>POST /api/evaluation/model-run</c>: how many labelled vacancies the other model analysed.</summary>
public sealed record ModelRunDto(string Model, int Vacancies, int Extracted, int Scored, long DurationMs);

/// <summary>
/// „eval-ranking" (step 07c): precision@10/20 and NDCG@20 of the ranking variants over the gold labels — rules only,
/// combined (current), KI-Bewertung only, rank + w × KI, and the same with another model. Read-only except the model
/// run, which fills only the caches (<c>VacancyExtractions</c>, <c>VacancyFitScores</c>), never a vacancy.
/// </summary>
public static partial class EvaluationEndpoints
{
    /// <summary>Weights w of „rank + w × KI" (normalised: (rank + w × KI) / (1 + w)).</summary>
    public static readonly double[] Weights = [0.25, 0.5, 1.0];

    public static IEndpointRouteBuilder MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/evaluation").WithTags("Evaluation");
        group.MapGet("/ranking", GetRanking).WithName("EvaluateRanking");
        group.MapPost("/model-run", RunModel).WithName("RunEvaluationModel");
        return app;
    }

    private static async Task<Results<Ok<RankingEvaluationDto>, ValidationProblem>> GetRanking(
        AppDbContext db,
        VacancyEnricher enricher,
        VacancyExtractionService extraction,
        VacancyFitScoreService fitScore,
        CancellationToken cancellationToken,
        bool @implicit = true,
        string? model = null)
    {
        if (model is not null && !ValidModel(model))
        {
            return ModelProblem();
        }

        var all = await LabelSet.LoadAsync(db, null, cancellationToken);
        var labels = all.Values.Where(l => @implicit || !l.Automatic).ToDictionary(l => l.VacancyId);
        var ids = labels.Keys.ToList();
        var vacancies = await db.Vacancies.AsNoTracking().Where(v => ids.Contains(v.Id)).ToListAsync(cancellationToken);
        var profile = fitScore.CurrentProfile();

        var rows = new List<EvaluationRowDto>();
        foreach (var vacancy in vacancies)
        {
            var label = labels[vacancy.Id];
            var stored = StoredFitScore.Parse(vacancy.AiScoreJson);
            var (rules, combined) = (vacancy.RuleRankScore ?? vacancy.RankScore, vacancy.RankScore);
            int? modelCombined = null, modelAi = null;
            if (model is not null && !string.IsNullOrWhiteSpace(vacancy.Description))
            {
                // The vacancy is not tracked: scoring it with the other model's extraction changes nothing in the database.
                if (await extraction.CachedWithAsync(vacancy.Description, model, cancellationToken) is { Status: ExtractionStatus.Succeeded } entry
                    && StoredExtraction.DeserializeResult(entry.ResultJson) is { } result)
                {
                    vacancy.ExtractionJson = new StoredExtraction(result, ExtractionKey.HashOf(vacancy.Description), entry.Model, entry.PromptVersion, entry.CreatedAt).ToJson();
                    enricher.Apply(vacancy);
                    modelCombined = vacancy.RankScore;
                }
                else
                {
                    modelCombined = rules;
                }

                if (profile is { } current
                    && await fitScore.CachedWithAsync(vacancy.Description, current.Hash, model, cancellationToken) is { Status: ExtractionStatus.Succeeded } score)
                {
                    modelAi = StoredFitScore.DeserializeResult(score.ResultJson)?.Score;
                }
            }

            rows.Add(new EvaluationRowDto(
                vacancy.Id, label.Fits, label.Automatic, rules, combined, vacancy.AiScore,
                modelCombined, modelAi, stored?.Result.Blockers ?? []));
        }

        var results = new List<RankingResult>
        {
            Evaluate("Nur Regeln", rows, r => r.Rules, r => r.Combined),
            Evaluate("Kombiniert (aktuell)", rows, r => r.Combined, r => r.Rules),
            Evaluate("Nur KI-Bewertung", rows, r => r.Ai, r => r.Combined),
        };
        results.AddRange(Weights.Select(w => Evaluate($"Rang + {Format(w)} × KI", rows, r => Blend(r.Combined, r.Ai, w), r => r.Combined)));
        if (model is not null)
        {
            results.Add(Evaluate($"Kombiniert ({model})", rows, r => r.ModelCombined, r => r.Rules));
            results.Add(Evaluate($"Nur KI-Bewertung ({model})", rows, r => r.ModelAi, r => r.ModelCombined));
            results.AddRange(Weights.Select(w => Evaluate($"Rang + {Format(w)} × KI ({model})", rows, r => Blend(r.ModelCombined, r.ModelAi, w), r => r.ModelCombined)));
        }

        return TypedResults.Ok(new RankingEvaluationDto(
            @implicit, all.Values.Count(l => !l.Automatic), all.Values.Count(l => l.Automatic), ExtractionEndpoints.LabelTarget, model, results,
            [.. rows.OrderByDescending(r => r.Combined ?? -1)]));
    }

    /// <summary>
    /// Extraction and KI-Bewertung of every labelled vacancy with <paramref name="model"/> (same provider as the tasks),
    /// one after the other — cached, so a second run is instant. Real model calls: only on request.
    /// </summary>
    private static async Task<Results<Ok<ModelRunDto>, ValidationProblem>> RunModel(
        string model,
        AppDbContext db,
        VacancyExtractionService extraction,
        VacancyFitScoreService fitScore,
        CancellationToken cancellationToken,
        bool @implicit = true)
    {
        if (!ValidModel(model))
        {
            return ModelProblem();
        }

        var labels = await LabelSet.LoadAsync(db, null, cancellationToken);
        var ids = labels.Values.Where(l => @implicit || !l.Automatic).Select(l => l.VacancyId).ToList();
        var watch = Stopwatch.StartNew();
        var (extracted, scored) = (0, 0);
        foreach (var id in ids)
        {
            extracted += await extraction.ExtractWithAsync(id, model, cancellationToken) is null ? 0 : 1;
            scored += await fitScore.ScoreWithAsync(id, model, cancellationToken) is null ? 0 : 1;
        }

        return TypedResults.Ok(new ModelRunDto(model, ids.Count, extracted, scored, watch.ElapsedMilliseconds));
    }

    private static RankingResult Evaluate(string variant, IEnumerable<EvaluationRowDto> rows, Func<EvaluationRowDto, double?> score, Func<EvaluationRowDto, double?> tieBreak) =>
        RankingMetrics.Evaluate(variant, [.. rows.Select(r => new ScoredLabel(r.VacancyId, r.Fits, score(r), tieBreak(r) ?? 0))]);

    /// <summary>(rank + w × KI) / (1 + w); without a KI score the rank alone.</summary>
    internal static double? Blend(int? rank, int? ai, double weight) =>
        rank is null ? ai : ai is null ? rank : (rank.Value + (weight * ai.Value)) / (1 + weight);

    private static string Format(double weight) => weight.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool ValidModel(string model) => model.Length <= 100 && ModelName().IsMatch(model);

    private static ValidationProblem ModelProblem() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["model"] = ["Model must look like 'qwen3.5:9b' (letters, digits, . : - _ /)."] });

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:/-]*$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex ModelName();
}
