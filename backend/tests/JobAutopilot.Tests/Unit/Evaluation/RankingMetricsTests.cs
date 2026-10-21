using FluentAssertions;
using JobAutopilot.Core.Evaluation;

namespace JobAutopilot.Tests.Unit.Evaluation;

public class RankingMetricsTests
{
    [Fact]
    public void Precision_counts_fits_in_the_top_k()
    {
        bool[] ordered = [true, false, true, true, false];

        RankingMetrics.PrecisionAt(ordered, 2).Should().Be(0.5);
        RankingMetrics.PrecisionAt(ordered, 10).Should().Be(0.6, "fewer labels than k: divided by the labels there are");
        RankingMetrics.PrecisionAt([], 10).Should().Be(0);
    }

    [Fact]
    public void Ndcg_is_one_for_the_ideal_order_and_lower_when_a_fit_comes_late()
    {
        RankingMetrics.NdcgAt([true, true, false], 20).Should().Be(1);
        RankingMetrics.NdcgAt([false, false, false], 20).Should().Be(0);
        // DCG = 1/log2(3) = 0.631; ideal = 1.
        RankingMetrics.NdcgAt([false, true, false], 20).Should().BeApproximately(0.631, 0.001);
    }

    [Fact]
    public void Vacancies_without_a_score_go_last_and_ties_use_the_tie_break()
    {
        var a = new ScoredLabel(Guid.NewGuid(), true, null, 99);
        var b = new ScoredLabel(Guid.NewGuid(), false, 50, 10);
        var c = new ScoredLabel(Guid.NewGuid(), true, 50, 20);

        RankingMetrics.Order([a, b, c]).Should().Equal(c, b, a);
        RankingMetrics.Evaluate("x", [a, b, c]).Should().Be(new RankingResult("x", 3, 2, 2 / 3.0, 2 / 3.0, (1 + 1 / Math.Log2(4)) / (1 + 1 / Math.Log2(3))));
    }
}
