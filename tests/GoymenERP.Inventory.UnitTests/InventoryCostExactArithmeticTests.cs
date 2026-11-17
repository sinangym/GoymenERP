using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryCostExactArithmeticTests
{
    [Theory]
    [InlineData("1", "3")]
    [InlineData("2", "3")]
    public void A_nonrepresentable_scalar_projection_is_refused_while_its_ratio_remains_exact(string numerator, string denominator)
    {
        var a = decimal.Parse(numerator, System.Globalization.CultureInfo.InvariantCulture);
        var b = decimal.Parse(denominator, System.Globalization.CultureInfo.InvariantCulture);
        Assert.False(InventoryCostExactArithmetic.TryDivide(a, b, out _));
        var ratio = ExactCostRatio.Of(a).Divide(b);
        Assert.Equal(numerator, ratio.Numerator); Assert.Equal(denominator, ratio.Denominator);
    }

    [Fact]
    public void A_one_third_receipt_rounds_only_the_final_average_and_retains_its_original_one_lira_value()
    {
        var unit = ExactCostRatio.Of(1m).Divide(3m);
        var received = AverageCostCalculator.Apply(null, new(AverageCostMovementKind.PurchaseReceipt, 3m, 0m), unit);
        Assert.Equal((3m, 0.3333m), (received.QuantityKg, received.AverageCostPerKg));
        Assert.True(unit.Multiply(3m).TryToDecimal(out var original)); Assert.Equal(1m, original);
        // Reversal removes the pool effect actually written, preserving its separately recorded rounding difference.
        var actualPoolEffect = ExactCostRatio.Of(received.QuantityKg!.Value).Multiply(received.AverageCostPerKg!.Value);
        var reversed = AverageCostCalculator.Apply(received, new(AverageCostMovementKind.PurchaseReturn, 3m, 3m), actualPoolEffect.Divide(3m));
        Assert.Equal((0m, 0.3333m), (reversed.QuantityKg, reversed.AverageCostPerKg));
        Assert.True(actualPoolEffect.Subtract(unit.Multiply(3m)).TryToDecimal(out var delta)); Assert.Equal(-0.0001m, delta);
    }

    [Fact]
    public void A_supplier_return_uses_the_original_unit_value_without_the_generic_issue_average()
    {
        var after = AverageCostCalculator.Apply(AverageCostPosition.Known(100m, 12m),
            new(AverageCostMovementKind.PurchaseReturn, 20m, 100m), ExactCostRatio.Of(10m));
        Assert.Equal((80m, 12.5m), (after.QuantityKg, after.AverageCostPerKg));
    }

    [Fact]
    public void A_positive_receipt_can_have_a_signed_recorded_pool_effect_that_reverses_without_a_negative_price()
    {
        var before = AverageCostPosition.Known(10000m, 1m);
        var after = AverageCostCalculator.Apply(before, new(AverageCostMovementKind.PurchaseReceipt, 1m, 10000m, 0.01m));
        var delta = ExactCostRatio.Of(after.QuantityKg!.Value).Multiply(after.AverageCostPerKg!.Value).Subtract(ExactCostRatio.Of(10000m));
        Assert.Equal(-1, delta.Sign);
        var reversed = AverageCostCalculator.ReverseRecordedPoolEffect(after, 10001m, 1m, delta);
        Assert.Equal((10000m, 1m), (reversed.QuantityKg, reversed.AverageCostPerKg));
        var zero = AverageCostCalculator.ReverseRecordedPoolEffect(AverageCostPosition.Known(1m, 0m), 1m, 1m, ExactCostRatio.Of(0m));
        Assert.Equal((0m, 0m), (zero.QuantityKg, zero.AverageCostPerKg));
    }

    [Fact]
    public void Restoring_a_known_zero_issue_preserves_its_value_while_a_new_zero_price_receipt_remains_unknown()
    {
        var returned = AverageCostCalculator.RestoreKnownMovementValue(AverageCostPosition.Known(0m, 0m), 0m, 1m, ExactCostRatio.Of(0m));
        Assert.Equal((1m, 0m), (returned.QuantityKg, returned.AverageCostPerKg));
        var unknown = AverageCostCalculator.Apply(null, new(AverageCostMovementKind.PurchaseReceipt, 1m, 0m, 0m));
        Assert.Equal((AverageCostStatus.Unreadable, AverageCostUnreadableReason.ZeroCostReceipt), (unknown.Status, unknown.UnreadableReason));
    }

    [Fact]
    public void Exact_decimal_costs_preserve_all_significant_digits_and_refuse_overflow_and_scale_loss()
    {
        Assert.True(InventoryCostExactArithmetic.TryDivide(1m, 8m, out var value)); Assert.Equal(0.125m, value);
        Assert.True(InventoryCostExactArithmetic.TryDivide(0.0000000000000000000000000001m, 1m, out value));
        Assert.Equal(0.0000000000000000000000000001m, value);
        Assert.False(InventoryCostExactArithmetic.TryDivide(0.0000000000000000000000000001m, 10m, out _));
        Assert.False(InventoryCostExactArithmetic.TryDivide(decimal.MaxValue, 0.1m, out _));
        Assert.False(InventoryCostExactArithmetic.TryDivide(1m, 0m, out _));
        Assert.True(InventoryCostExactArithmetic.TryMultiply(0.125m, 8m, out value)); Assert.Equal(1m, value);
        Assert.False(InventoryCostExactArithmetic.TryMultiply(0.0000000000000000000000000001m, 0.1m, out _));
    }
}
