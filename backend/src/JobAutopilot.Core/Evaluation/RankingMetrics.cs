namespace JobAutopilot.Core.Evaluation;

/// <summary>One labelled vacancy with the score a ranking variant gives it (<c>null</c> = no score: ranked last).</summary>
public sealed record ScoredLabel(Guid VacancyId, bool Fits, double? Score, double TieBreak = 0);

/// <summary>precision@10, precision@20 and NDCG@20 of one variant over the labelled vacancies.</summary>
public sealed record RankingResult(string Variant, int Count, int Fits, double PrecisionAt10, double PrecisionAt20, double NdcgAt20);

/// <summary>
/// Ranking metrics over the labelled vacancies only (step 07c): each variant orders them by its score, „Passt" is
/// relevant (1), „Passt nicht" is not (0). With fewer than k labels, precision@k divides by the labels there are, so
/// a short list is not punished for its length — compare variants with each other, not with another label set.
/// </summary>
public static class RankingMetrics
{
    public static RankingResult Evaluate(string variant, IReadOnlyCollection<ScoredLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var ordered = Order(labels).Select(l => l.Fits).ToList();
        return new RankingResult(variant, ordered.Count, ordered.Count(f => f), PrecisionAt(ordered, 10), PrecisionAt(ordered, 20), NdcgAt(ordered, 20));
    }

    /// <summary>Highest score first, no score last; ties by <see cref="ScoredLabel.TieBreak"/>, then id (stable).</summary>
    public static IEnumerable<ScoredLabel> Order(IEnumerable<ScoredLabel> labels) =>
        labels.OrderBy(l => l.Score is null)
            .ThenByDescending(l => l.Score ?? 0)
            .ThenByDescending(l => l.TieBreak)
            .ThenBy(l => l.VacancyId);

    public static double PrecisionAt(IReadOnlyList<bool> ordered, int k)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        var top = ordered.Take(k).ToList();
        return top.Count == 0 ? 0 : (double)top.Count(f => f) / top.Count;
    }

    /// <summary>Binary NDCG: DCG of the order divided by the DCG of the ideal order (all „Passt" first).</summary>
    public static double NdcgAt(IReadOnlyList<bool> ordered, int k)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        static double Dcg(IEnumerable<bool> relevant) => relevant.Select((r, i) => r ? 1 / Math.Log2(i + 2) : 0).Sum();
        var ideal = Dcg(ordered.OrderByDescending(f => f).Take(k));
        return ideal == 0 ? 0 : Dcg(ordered.Take(k)) / ideal;
    }
}
