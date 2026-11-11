using GoymenERP.Modules.Inventory.Domain.Costing;
using static GoymenERP.Modules.Inventory.Domain.Costing.AverageCostMovementKind;
using static GoymenERP.Modules.Inventory.Domain.Costing.AverageCostUnreadableReason;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D262..D267 (SINAN_KARARLARI_2026-11-10_SATIS_EK52), the deterministic moving-average purchase cost core: golden
/// examples at the 4-decimal internal scale (AwayFromZero), the transfer rule, the production rule, and every undecided
/// point (OPEN-INV-AVGCOST-*) answered Unreadable instead of a guess. SENTETIK data only.
/// </summary>
public sealed class InventoryAverageCostCalculatorTests
{
    private static AverageCostMovement Purchase(decimal qty, decimal before, decimal cost) =>
        new(PurchaseReceipt, qty, before, cost);

    private static void AssertKnown(AverageCostPosition position, decimal qty, decimal average)
    {
        Assert.Equal(AverageCostStatus.Known, position.Status);
        Assert.Equal((qty, average), (position.QuantityKg, position.AverageCostPerKg));
    }

    [Fact]
    public void The_first_purchase_receipt_sets_the_average_to_its_own_cost()
    {
        AssertKnown(AverageCostCalculator.Apply(null, Purchase(100m, 0m, 10m)), 100m, 10m);
    }

    [Fact]
    public void A_purchase_receipt_recalculates_the_weighted_average()
    {
        var first = AverageCostCalculator.Apply(null, Purchase(100m, 0m, 10m));
        AssertKnown(AverageCostCalculator.Apply(first, Purchase(50m, 100m, 13m)), 150m, 11.0000m);
    }

    [Fact]
    public void The_average_is_rounded_to_four_decimals_and_the_remainder_is_never_carried()
    {
        var first = AverageCostCalculator.Apply(null, Purchase(3m, 0m, 10m));
        // (30 + 44) / 7 = 10.571428... -> 10.5714
        AssertKnown(AverageCostCalculator.Apply(first, Purchase(4m, 3m, 11m)), 7m, 10.5714m);
    }

    [Theory]
    [InlineData("0.00025", "0.0003")]
    [InlineData("0.00024", "0.0002")]
    [InlineData("10.00005", "10.0001")]
    [InlineData("10.00004", "10.0000")]
    public void Rounding_is_away_from_zero_at_the_midpoint(string cost, string expected)
    {
        AssertKnown(AverageCostCalculator.Apply(null, Purchase(1m, 0m, decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture))),
            1m, decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void An_issue_keeps_the_average_and_lowers_the_quantity()
    {
        var held = AverageCostPosition.Known(150m, 11m);
        AssertKnown(AverageCostCalculator.Apply(held, new(Issue, 40m, 150m)), 110m, 11m);
    }

    [Fact]
    public void Zero_stock_keeps_the_last_average_and_the_next_receipt_restarts_from_its_own_cost()
    {
        // Sinan 2026-11-11 (SAT-D296): at zero stock the last average stays readable (a Value, not Unreadable).
        var empty = AverageCostCalculator.Apply(AverageCostPosition.Known(5m, 8m), new(Issue, 5m, 5m));
        AssertKnown(empty, 0m, 8m);
        AssertKnown(AverageCostCalculator.Apply(empty, Purchase(2m, 0m, 20m)), 2m, 20m);
    }

    [Fact]
    public void A_zero_cost_purchase_is_cost_unknown_and_never_enters_the_average()
    {
        // Sinan 2026-11-11 (SAT-D297): zero unit cost is "cost unknown" (Unreadable), sticky like the other reasons.
        var first = AverageCostCalculator.Apply(null, Purchase(5m, 0m, 0m));
        Assert.Equal(AverageCostStatus.Unreadable, first.Status);
        Assert.Equal(ZeroCostReceipt, first.UnreadableReason);

        var held = AverageCostPosition.Known(10m, 12m);
        var after = AverageCostCalculator.Apply(held, Purchase(4m, 10m, 0m));
        Assert.Equal(ZeroCostReceipt, after.UnreadableReason);
        Assert.Equal(ZeroCostReceipt, AverageCostCalculator.Apply(after, Purchase(1m, 14m, 9m)).UnreadableReason);
    }

    [Fact]
    public void A_transfer_enters_the_target_at_the_source_average_and_blends_with_the_target_stock()
    {
        var source = AverageCostPosition.Known(7m, 10.5714m);
        var target = AverageCostPosition.Known(3m, 12m);

        var result = AverageCostCalculator.ApplyTransfer(source, 7m, target, 3m, 2m);

        // (3 * 12 + 2 * 10.5714) / 5 = 11.42856 -> 11.4286
        AssertKnown(result.Source, 5m, 10.5714m);
        AssertKnown(result.Target, 5m, 11.4286m);
    }

    [Fact]
    public void A_transfer_into_an_empty_target_takes_the_source_average_unchanged()
    {
        var result = AverageCostCalculator.ApplyTransfer(AverageCostPosition.Known(10m, 9.5m), 10m, null, 0m, 4m);

        AssertKnown(result.Source, 6m, 9.5m);
        AssertKnown(result.Target, 4m, 9.5m);
    }

    [Theory]
    [InlineData("no-source")]
    [InlineData("unreadable-source")]
    [InlineData("quantity-above-source")]
    public void A_transfer_from_a_source_without_a_readable_average_leaves_the_target_unreadable(string fault)
    {
        AverageCostPosition? source = fault switch
        {
            "no-source" => null,
            "unreadable-source" => AverageCostPosition.Unreadable(ForeignCurrency),
            _ => AverageCostPosition.Known(1m, 5m),
        };
        var sourceOnHand = fault == "no-source" ? 0m : (source!.QuantityKg ?? 0m);

        var result = AverageCostCalculator.ApplyTransfer(source, sourceOnHand, null, 0m, 2m);

        Assert.Equal(AverageCostStatus.Unreadable, result.Target.Status);
        Assert.Equal(TransferSourceUnreadable, result.Target.UnreadableReason);
    }

    [Fact]
    public void An_issue_beyond_the_held_quantity_is_negative_stock_and_unreadable_not_clamped()
    {
        // OPEN-INV-AVGCOST-NEGATIVE-STOCK: no policy chosen.
        var result = AverageCostCalculator.Apply(AverageCostPosition.Known(5m, 8m), new(Issue, 6m, 5m));
        Assert.Equal((AverageCostStatus.Unreadable, NegativeStock), (result.Status, result.UnreadableReason));
        Assert.Null(result.AverageCostPerKg);
        Assert.Null(result.QuantityKg);
    }

    [Fact]
    public void An_issue_from_a_position_the_ledger_never_costed_is_negative_stock_unreadable()
    {
        var result = AverageCostCalculator.Apply(null, new(Issue, 1m, 0m));
        Assert.Equal(NegativeStock, result.UnreadableReason);
    }

    [Fact]
    public void A_production_receipt_never_enters_the_average_and_leaves_the_position_unreadable()
    {
        // OPEN-INV-AVGCOST-PRODUCTION: mixed purchased/produced stock has no decided cost.
        var result = AverageCostCalculator.Apply(AverageCostPosition.Known(5m, 8m), new(ProductionReceipt, 2m, 5m));
        Assert.Equal((AverageCostStatus.Unreadable, ProductionSource), (result.Status, result.UnreadableReason));
    }

    [Fact]
    public void A_return_is_unreadable_until_its_effect_is_decided()
    {
        var result = AverageCostCalculator.Apply(AverageCostPosition.Known(5m, 8m), new(AverageCostMovementKind.Return, 1m, 5m));
        Assert.Equal(AverageCostUnreadableReason.Return, result.UnreadableReason);
    }

    [Theory]
    [InlineData("unit", UnitConversion)]
    [InlineData("fx", ForeignCurrency)]
    [InlineData("landed", LandedOrAdditionalCost)]
    public void An_undecided_receipt_input_is_unreadable_instead_of_a_guess(string fault, AverageCostUnreadableReason expected)
    {
        var movement = fault switch
        {
            "unit" => Purchase(1m, 0m, 10m) with { UnitCostIsPerStockKg = false },
            "fx" => Purchase(1m, 0m, 10m) with { IsBaseCurrency = false },
            _ => Purchase(1m, 0m, 10m) with { IncludesAdditionalCost = true },
        };

        var result = AverageCostCalculator.Apply(null, movement);

        Assert.Equal((AverageCostStatus.Unreadable, expected), (result.Status, result.UnreadableReason));
    }

    [Fact]
    public void Stock_the_ledger_never_costed_is_not_blended_into_the_average()
    {
        // OPEN-INV-AVGCOST-UNCOSTED-STOCK: 10 kg are on hand but the ledger tracks none.
        var result = AverageCostCalculator.Apply(null, Purchase(5m, 10m, 12m));
        Assert.Equal(LedgerStockMismatch, result.UnreadableReason);

        var tracked = AverageCostCalculator.Apply(AverageCostPosition.Known(5m, 8m), Purchase(5m, 7m, 12m));
        Assert.Equal(LedgerStockMismatch, tracked.UnreadableReason);
    }

    [Fact]
    public void An_unreadable_position_is_sticky_for_every_later_movement()
    {
        var unreadable = AverageCostPosition.Unreadable(ForeignCurrency);
        Assert.Same(unreadable, AverageCostCalculator.Apply(unreadable, Purchase(1m, 0m, 10m)));
        Assert.Same(unreadable, AverageCostCalculator.Apply(unreadable, new(Issue, 1m, 0m)));
    }

    [Fact]
    public void The_calculation_is_deterministic()
    {
        var held = AverageCostPosition.Known(3m, 10m);
        Assert.Equal(
            AverageCostCalculator.Apply(held, Purchase(4m, 3m, 11m)),
            AverageCostCalculator.Apply(held, Purchase(4m, 3m, 11m)));
    }

    [Fact]
    public void Invalid_movements_are_refused()
    {
        Assert.Throws<ArgumentNullException>(() => AverageCostCalculator.Apply(null, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostCalculator.Apply(null, Purchase(0m, 0m, 1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostCalculator.Apply(null, Purchase(-1m, 0m, 1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostCalculator.Apply(null, Purchase(1m, -1m, 1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostCalculator.Apply(null, Purchase(1m, 0m, -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostCalculator.Apply(null, new(PurchaseReceipt, 1m, 0m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostCalculator.Apply(null, new((AverageCostMovementKind)99, 1m, 0m)));
    }

    [Fact]
    public void A_position_is_known_with_non_negative_four_decimal_figures_or_unreadable_without_numbers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostPosition.Known(-1m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostPosition.Known(1m, -1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostPosition.Known(1m, 1.00001m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostPosition.Unreadable((AverageCostUnreadableReason)0));
        var unreadable = AverageCostPosition.Unreadable(NegativeStock);
        Assert.Equal((null, null), (unreadable.QuantityKg, unreadable.AverageCostPerKg));
    }

    [Fact]
    public void The_persisted_reason_numbers_are_pinned()
    {
        Assert.Equal(
            [1, 2, 3, 4, 5, 6, 7, 8, 9],
            Enum.GetValues<AverageCostUnreadableReason>().Select(reason => (int)reason).Order());
        Assert.Equal((1, 2), ((int)AverageCostStatus.Known, (int)AverageCostStatus.Unreadable));
        Assert.Equal(4, AverageCostPrecision.InternalScale);
    }
}
