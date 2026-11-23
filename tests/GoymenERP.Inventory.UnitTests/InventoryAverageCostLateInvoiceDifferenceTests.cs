using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D385 / SAT-D404 (STK-SC36-a): a late supplier invoice's TL cost difference joins the current TL average once, at the
/// invoice, without resequencing history or changing the quantity. Unreadable positions stay sticky; a difference with no
/// stock to join or one that would leave a negative value is never written. SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryAverageCostLateInvoiceDifferenceTests
{
    [Fact]
    public void Late_invoice_status_numbers_are_pinned()
    {
        Assert.Equal((1, 2, 3, 4), ((int)AverageCostLateInvoiceDifferenceStatus.Absorbed,
            (int)AverageCostLateInvoiceDifferenceStatus.PositionUnreadable,
            (int)AverageCostLateInvoiceDifferenceStatus.NoStockToAbsorb,
            (int)AverageCostLateInvoiceDifferenceStatus.DifferenceExceedsValue));
    }

    [Fact]
    public void The_difference_joins_the_current_average_once_and_the_quantity_is_unchanged()
    {
        // Received 100 kg at 40.0000; 60 kg issued since; 140 kg held at 41.0000. The invoice says 42.0000:
        // (140 · 41 + 100 · 2) / 140 = 42.428571.. -> 42.4286. The issued kilograms are not revalued.
        var current = AverageCostPosition.Known(140m, 41m);

        var result = AverageCostLateInvoiceDifferenceCalculator.Apply(current, 100m, 40m, 42m);

        Assert.Equal(AverageCostLateInvoiceDifferenceStatus.Absorbed, result.Status);
        Assert.Equal(200m, result.DifferenceValue);
        Assert.Equal((AverageCostStatus.Known, (decimal?)140m, (decimal?)42.4286m),
            (result.Position.Status, result.Position.QuantityKg, result.Position.AverageCostPerKg));
    }

    [Fact]
    public void A_cheaper_invoice_lowers_the_average_and_an_equal_one_changes_nothing()
    {
        var current = AverageCostPosition.Known(50m, 30m);

        var lower = AverageCostLateInvoiceDifferenceCalculator.Apply(current, 20m, 31m, 30.5m);
        Assert.Equal((AverageCostLateInvoiceDifferenceStatus.Absorbed, -10m, (decimal?)29.8m),
            (lower.Status, lower.DifferenceValue, lower.Position.AverageCostPerKg));

        var same = AverageCostLateInvoiceDifferenceCalculator.Apply(current, 20m, 31m, 31m);
        Assert.Equal((AverageCostLateInvoiceDifferenceStatus.Absorbed, 0m), (same.Status, same.DifferenceValue));
        Assert.Same(current, same.Position);
    }

    [Fact]
    public void An_unreadable_position_stays_sticky()
    {
        var unreadable = AverageCostPosition.Unreadable(AverageCostUnreadableReason.ForeignCurrency);

        var result = AverageCostLateInvoiceDifferenceCalculator.Apply(unreadable, 10m, 40m, 42m);

        Assert.Equal(AverageCostLateInvoiceDifferenceStatus.PositionUnreadable, result.Status);
        Assert.Same(unreadable, result.Position);
    }

    [Fact]
    public void No_stock_or_a_credit_larger_than_the_value_is_never_written()
    {
        var empty = AverageCostPosition.Known(0m, 41m);
        var noStock = AverageCostLateInvoiceDifferenceCalculator.Apply(empty, 100m, 40m, 42m);
        Assert.Equal(AverageCostLateInvoiceDifferenceStatus.NoStockToAbsorb, noStock.Status);
        Assert.Same(empty, noStock.Position);

        // 5 kg held at 1.0000 (value 5); a credit of 100 · (10 − 1) = −900 would leave a negative average.
        var thin = AverageCostPosition.Known(5m, 1m);
        var exceeds = AverageCostLateInvoiceDifferenceCalculator.Apply(thin, 100m, 10m, 1m);
        Assert.Equal((AverageCostLateInvoiceDifferenceStatus.DifferenceExceedsValue, -900m), (exceeds.Status, exceeds.DifferenceValue));
        Assert.Same(thin, exceeds.Position);
    }

    [Fact]
    public void Unknown_costs_and_non_positive_quantities_are_caller_errors()
    {
        var current = AverageCostPosition.Known(10m, 1m);
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostLateInvoiceDifferenceCalculator.Apply(current, 0m, 1m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostLateInvoiceDifferenceCalculator.Apply(current, 1m, 0m, 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AverageCostLateInvoiceDifferenceCalculator.Apply(current, 1m, 1m, 0m));
    }
}
