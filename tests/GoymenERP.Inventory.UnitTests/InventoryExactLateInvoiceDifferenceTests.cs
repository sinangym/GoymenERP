using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryExactLateInvoiceDifferenceTests
{
    [Theory]
    [InlineData(20, 5.2)]
    [InlineData(-20, 4.8)]
    [InlineData(0, 5)]
    public void Known_cost_difference_changes_only_the_current_average_and_preserves_the_exact_source(decimal difference, decimal average)
    {
        var before = new ExactAverageCostPool(ExactCostRatio.Of(100m), 5m);
        var result = AverageCostLateInvoiceDifferenceCalculator.ApplyExact(before, ExactCostRatio.Of(difference));
        Assert.Equal(average, result.Position.AverageCostPerKg);
        Same(before.QuantityKg, result.Position.QuantityKg);
        Same(ExactCostRatio.Of(difference), result.AbsorbedDifference);
        Assert.Equal(0, result.UnabsorbedDifference.Sign);
        Conserved(result);
        Assert.Equal(5m, before.AverageCostPerKg);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(-25)]
    public void Empty_current_pool_preserves_the_whole_signed_difference_for_SMM(decimal difference)
    {
        var before = new ExactAverageCostPool(ExactCostRatio.Of(0m), 7m);
        var result = AverageCostLateInvoiceDifferenceCalculator.ApplyExact(before, ExactCostRatio.Of(difference));
        Assert.Same(before, result.Position);
        Assert.Equal(0, result.AbsorbedDifference.Sign);
        Same(ExactCostRatio.Of(difference), result.UnabsorbedDifference);
        Assert.Equal(0, result.RecordedPoolValueEffect.Sign);
        Conserved(result);
    }

    [Fact]
    public void Credit_larger_than_current_value_absorbs_only_to_zero_and_preserves_the_signed_remainder()
    {
        var result = AverageCostLateInvoiceDifferenceCalculator.ApplyExact(
            new(ExactCostRatio.Of(3m), 10m), ExactCostRatio.Of(-40m));
        Assert.Equal(0m, result.Position.AverageCostPerKg);
        Same(ExactCostRatio.Of(3m), result.Position.QuantityKg);
        Same(ExactCostRatio.Of(-30m), result.AbsorbedDifference);
        Same(ExactCostRatio.Of(-10m), result.UnabsorbedDifference);
        Conserved(result);
    }

    [Fact]
    public void Nonterminating_quantity_and_difference_keep_exact_evidence_with_only_the_final_average_rounded()
    {
        var quantity = ExactCostRatio.Of(1m).Divide(3m);
        var difference = ExactCostRatio.Of(1m).Divide(7m);
        var result = AverageCostLateInvoiceDifferenceCalculator.ApplyExact(new(quantity, 10m), difference);
        Assert.Equal(10.4286m, result.Position.AverageCostPerKg);
        Assert.False(result.SourceDifference.TryToDecimal(out _));
        Assert.NotEqual(0, result.AverageRoundingDifference.Sign);
        Same(difference, result.AbsorbedDifference);
        Conserved(result);
    }

    [Fact]
    public void Fractional_current_value_cap_is_not_rounded_into_a_different_SMM_amount()
    {
        var quantity = ExactCostRatio.Of(1m).Divide(3m);
        var result = AverageCostLateInvoiceDifferenceCalculator.ApplyExact(new(quantity, 10m), ExactCostRatio.Of(-4m));
        Same(ExactCostRatio.Of(-10m).Divide(3m), result.AbsorbedDifference);
        Same(ExactCostRatio.Of(-2m).Divide(3m), result.UnabsorbedDifference);
        Assert.False(result.UnabsorbedDifference.TryToDecimal(out _));
        Assert.Equal(0m, result.Position.AverageCostPerKg);
        Conserved(result);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 1.00001)]
    public void Invalid_or_unrecorded_precision_pool_is_not_repaired_by_invoice_difference(decimal quantity, decimal average)
    {
        Assert.Throws<ArgumentException>(() => AverageCostLateInvoiceDifferenceCalculator.ApplyExact(
            new(ExactCostRatio.Of(quantity), average), ExactCostRatio.Of(1m)));
    }

    private static void Conserved(ExactAverageCostLateInvoiceDifferenceResult result)
    {
        Same(result.SourceDifference, result.AbsorbedDifference.Add(result.UnabsorbedDifference));
        Same(result.RecordedPoolValueEffect, result.AbsorbedDifference.Add(result.AverageRoundingDifference));
    }
    private static void Same(ExactCostRatio expected, ExactCostRatio actual) => Assert.Equal(0, expected.CompareTo(actual));
}
