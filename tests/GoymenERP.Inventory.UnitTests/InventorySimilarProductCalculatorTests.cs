using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D70 / ASSUMPTION-STK-SIMILAR-ORDER-1: the one deterministic "benzer ürün" rule. Tolerances here are synthetic
/// test inputs; the real values live only in company Settings.
/// </summary>
public sealed class InventorySimilarProductCalculatorTests
{
    private static readonly Guid Requested = Guid.Parse("21000000-0000-0000-0000-000000000001");
    private static readonly Guid Candidate = Guid.Parse("21000000-0000-0000-0000-000000000002");
    private static readonly InventorySimilarProductToleranceV1 TenPercent = new(10m, 10m, 10m);

    [Fact]
    public void No_tolerance_means_nothing_is_similar()
    {
        Assert.Null(InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 1m, 100m, null),
            Facts(Candidate, 1m, 100m, null), null));
    }

    [Fact]
    public void A_malformed_tolerance_is_never_applied()
    {
        Assert.False(new InventorySimilarProductToleranceV1(100m, 1m, 1m).IsWellFormed);
        Assert.False(new InventorySimilarProductToleranceV1(-1m, 1m, 1m).IsWellFormed);
        Assert.False(new InventorySimilarProductToleranceV1(1.00001m, 1m, 1m).IsWellFormed);
        Assert.Null(InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 1m, 100m, null),
            Facts(Candidate, 1m, 100m, null), new InventorySimilarProductToleranceV1(100m, 10m, 10m)));
    }

    [Fact]
    public void Different_similarity_keys_or_missing_facts_are_never_similar()
    {
        Assert.Null(InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 1m, 100m, null),
            Facts(Candidate, 1m, 100m, null, key: "other"), TenPercent));
        Assert.Null(InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 1m, 100m, null),
            Facts(Candidate, 1m, 100m, null, key: null), TenPercent));
        Assert.Null(InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 1m, 100m, null),
            Facts(Requested, 1m, 100m, null), TenPercent));
    }

    [Fact]
    public void Each_dimension_is_compared_with_its_own_symmetric_percentage_inclusive_at_the_edge()
    {
        var tolerance = new InventorySimilarProductToleranceV1(10m, 5m, 0m);
        var atEdge = InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 1m, 100m, 2000m),
            Facts(Candidate, 0.9m, 105m, 2000m), tolerance);

        Assert.NotNull(atEdge);
        Assert.Equal((0.1m, 0.15m, 3), (atEdge.MaximumDeviation, atEdge.TotalDeviation, atEdge.ComparedDimensions));
        Assert.Equal("10.00", atEdge.MaximumDeviationPercentText);
        Assert.Null(InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 1m, 100m, 2000m),
            Facts(Candidate, 1m, 105.01m, 2000m), tolerance));
        Assert.Null(InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 1m, 100m, 2000m),
            Facts(Candidate, 1m, 100m, 2001m), tolerance));
    }

    [Fact]
    public void A_missing_dimension_is_skipped_and_no_comparable_dimension_is_not_similar()
    {
        var skipped = InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 1m, 100m, null),
            Facts(Candidate, 1.05m, 100m, 3000m), TenPercent);

        Assert.NotNull(skipped);
        Assert.Equal(2, skipped.ComparedDimensions);
        Assert.Null(InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, null, null, null),
            Facts(Candidate, null, null, null), TenPercent));
        Assert.Null(InventorySimilarProductCalculatorV1.Evaluate(Facts(Requested, 0m, 100m, null),
            Facts(Candidate, 1m, 100m, null), TenPercent));
    }

    [Fact]
    public void Candidates_are_ordered_by_largest_then_summed_deviation_then_label_code()
    {
        var closer = new InventorySimilarityScoreV1(0.02m, 0.03m, 2);
        var widerTotal = new InventorySimilarityScoreV1(0.02m, 0.04m, 2);
        var farther = new InventorySimilarityScoreV1(0.05m, 0.05m, 1);

        Assert.True(InventorySimilarProductCalculatorV1.Compare(closer, "B", farther, "A") < 0);
        Assert.True(InventorySimilarProductCalculatorV1.Compare(closer, "B", widerTotal, "A") < 0);
        Assert.True(InventorySimilarProductCalculatorV1.Compare(closer, "A", closer, "B") < 0);
        Assert.Equal(0, InventorySimilarProductCalculatorV1.Compare(closer, "A", closer, "A"));
    }

    private static InventoryProductSimilarityFactsV1 Facts(
        Guid productId,
        decimal? thickness,
        decimal? width,
        decimal? length,
        string? key = "synthetic-key") =>
        new(productId, "1", "SYN-P", "Sentetik ürün", key, thickness, width, length);
}
