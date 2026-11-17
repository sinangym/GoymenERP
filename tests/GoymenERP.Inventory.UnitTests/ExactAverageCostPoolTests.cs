using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Inventory.UnitTests;

public sealed class ExactAverageCostPoolTests
{
    [Fact]
    public void An_issue_from_a_mixed_label_removes_only_the_exact_priced_material_share()
    {
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(2m), 10m);
        var pricedShare = Fraction(2m, 3m);

        var afterIssue = pool.Remove(pricedShare);

        Ratio(afterIssue.QuantityKg, "4", "3");
        Assert.Equal(10m, afterIssue.AverageCostPerKg);
        Ratio(afterIssue.Value, "40", "3");
        Ratio(afterIssue.QuantityKg.Add(pricedShare), "2", "1");
        Ratio(pool.QuantityKg, "2", "1");
        Ratio(pool.Value, "20", "1");
    }

    [Fact]
    public void Later_recognition_adds_the_exact_remaining_unpriced_share_and_rounds_only_the_final_average()
    {
        var priced = new ExactAverageCostPool(ExactCostRatio.Of(2m), 10m);
        var pricedAfterIssue = priced.Remove(Fraction(2m, 3m));
        var remainingUnpriced = Fraction(2m, 3m);

        var recognized = pricedAfterIssue.Add(remainingUnpriced, ExactCostRatio.Of(6.67m));

        Ratio(recognized.QuantityKg, "2", "1");
        Assert.Equal(10.0017m, recognized.AverageCostPerKg);
        Ratio(recognized.Value, "100017", "5000");
        Ratio(pricedAfterIssue.QuantityKg.Add(remainingUnpriced), "2", "1");
        Ratio(pricedAfterIssue.QuantityKg, "4", "3");
        Assert.Equal(10m, pricedAfterIssue.AverageCostPerKg);
    }

    [Fact]
    public void Reversing_a_negative_recorded_pool_effect_restores_the_original_quantity_and_average()
    {
        var before = new ExactAverageCostPool(ExactCostRatio.Of(10000m), 1m);
        var afterReceipt = before.Add(ExactCostRatio.Of(1m), ExactCostRatio.Of(0.01m));
        var recordedEffect = afterReceipt.Value.Subtract(before.Value);
        Assert.Equal(0.9999m, afterReceipt.AverageCostPerKg);
        Ratio(recordedEffect, "-1", "10000");

        var reversed = afterReceipt.Remove(ExactCostRatio.Of(1m), recordedEffect);

        Ratio(reversed.QuantityKg, "10000", "1");
        Assert.Equal(1m, reversed.AverageCostPerKg);
        Ratio(reversed.Value, "10000", "1");
    }

    [Fact]
    public void An_ordinary_issue_from_a_known_zero_value_pool_keeps_zero_as_a_known_recorded_average()
    {
        var knownResidual = new ExactAverageCostPool(ExactCostRatio.Of(100m), 0m);

        var partial = knownResidual.Remove(ExactCostRatio.Of(99m));
        var empty = partial.Remove(ExactCostRatio.Of(1m));

        Ratio(partial.QuantityKg, "1", "1");
        Ratio(partial.Value, "0", "1");
        Assert.Equal(0m, partial.AverageCostPerKg);
        Ratio(empty.QuantityKg, "0", "1");
        Ratio(empty.Value, "0", "1");
        Assert.Equal(0m, empty.AverageCostPerKg);
    }

    [Fact]
    public void An_admitted_zero_value_residual_can_enter_an_empty_priced_pool()
    {
        var empty = new ExactAverageCostPool(ExactCostRatio.Of(0m), 12m);

        var knownResidual = empty.Add(ExactCostRatio.Of(1m), ExactCostRatio.Of(0m));

        Ratio(knownResidual.QuantityKg, "1", "1");
        Ratio(knownResidual.Value, "0", "1");
        Assert.Equal(0m, knownResidual.AverageCostPerKg);
    }

    [Fact]
    public void A_whole_ordinary_issue_retains_the_last_average_at_zero_quantity()
    {
        var pool = new ExactAverageCostPool(Fraction(1m, 3m), 7.1234m);

        var empty = pool.Remove(Fraction(1m, 3m));

        Ratio(empty.QuantityKg, "0", "1");
        Ratio(empty.Value, "0", "1");
        Assert.Equal(7.1234m, empty.AverageCostPerKg);
    }

    [Fact]
    public void A_third_kilogram_roundtrip_preserves_exact_quantity_and_value_without_decimal_conversion()
    {
        var pool = new ExactAverageCostPool(Fraction(1m, 3m), 10m);
        var addedQuantity = Fraction(2m, 3m);
        var addedValue = Fraction(20m, 3m);

        var expanded = pool.Add(addedQuantity, addedValue);
        var restored = expanded.Remove(addedQuantity, addedValue);

        Ratio(expanded.QuantityKg, "1", "1");
        Assert.Equal(10m, expanded.AverageCostPerKg);
        Ratio(restored.QuantityKg, "1", "3");
        Ratio(restored.Value, "10", "3");
        Assert.Equal(10m, restored.AverageCostPerKg);
        Assert.False(restored.QuantityKg.TryToDecimal(out _));
    }

    [Fact]
    public void Reversing_a_whole_nonterminating_value_empties_the_pool_exactly()
    {
        var pool = new ExactAverageCostPool(Fraction(1m, 3m), 1m);

        var empty = pool.Remove(Fraction(1m, 3m), Fraction(1m, 3m));

        Ratio(empty.QuantityKg, "0", "1");
        Ratio(empty.Value, "0", "1");
        Assert.Equal(1m, empty.AverageCostPerKg);
    }

    [Fact]
    public void The_final_four_decimal_average_uses_the_existing_away_from_zero_midpoint_rule()
    {
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(1m), 1m);

        var after = pool.Add(ExactCostRatio.Of(1m), ExactCostRatio.Of(1.0001m));

        Ratio(after.QuantityKg, "2", "1");
        Assert.Equal(1.0001m, after.AverageCostPerKg);
    }

    [Fact]
    public void A_recorded_effect_that_would_make_remaining_value_negative_is_refused()
    {
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(2m), 10m);

        Assert.Throws<InvalidOperationException>(() => pool.Remove(ExactCostRatio.Of(1m), ExactCostRatio.Of(21m)));

        Ratio(pool.QuantityKg, "2", "1");
        Ratio(pool.Value, "20", "1");
    }

    [Theory]
    [InlineData(19)]
    [InlineData(21)]
    public void A_full_reversal_cannot_leave_positive_or_negative_residual_value(int actualValue)
    {
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(2m), 10m);

        Assert.Throws<InvalidOperationException>(() => pool.Remove(ExactCostRatio.Of(2m), ExactCostRatio.Of(actualValue)));
    }

    [Fact]
    public void A_full_reversal_with_the_actual_pool_value_retains_the_last_average()
    {
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(2m), 10m);

        var after = pool.Remove(ExactCostRatio.Of(2m), pool.Value);

        Ratio(after.QuantityKg, "0", "1");
        Ratio(after.Value, "0", "1");
        Assert.Equal(10m, after.AverageCostPerKg);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void A_zero_quantity_pool_cannot_retain_a_nonzero_effect_value(int actualValue)
    {
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(0m), 10m);

        Assert.Throws<InvalidOperationException>(() => pool.Remove(ExactCostRatio.Of(0m), ExactCostRatio.Of(actualValue)));
    }

    [Fact]
    public void Negative_or_excessive_issue_quantities_are_refused_without_mutating_the_pool()
    {
        var pool = new ExactAverageCostPool(Fraction(1m, 3m), 10m);

        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Remove(Fraction(-1m, 3m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Remove(Fraction(2m, 3m)));

        Ratio(pool.QuantityKg, "1", "3");
        Assert.Equal(10m, pool.AverageCostPerKg);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void Negative_added_quantity_or_value_is_refused(int quantity, int value)
    {
        var pool = new ExactAverageCostPool(ExactCostRatio.Of(1m), 10m);

        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Add(ExactCostRatio.Of(quantity), ExactCostRatio.Of(value)));
    }

    private static ExactCostRatio Fraction(decimal numerator, decimal denominator) => ExactCostRatio.Of(numerator).Divide(denominator);
    private static void Ratio(ExactCostRatio value, string numerator, string denominator) =>
        Assert.Equal((numerator, denominator), (value.Numerator, value.Denominator));
}
