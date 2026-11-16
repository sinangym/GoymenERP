using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Counts;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D331 / SAT-D334 (SINAN_KARARLARI_2026-11-13_EK1) golden cases for the pure count-difference cost rule: a surplus
/// and a shortage are valued at the location/product moving average, exactly and without changing it; an unreadable
/// or never-recorded average leaves a surplus "hesaplanamadı" and stops a shortage. The rule is not wired into the
/// approval until the average-cost ledger follows stock movements (SAT-D331 (5)).
/// </summary>
public sealed class InventoryStockCountCostTests
{
    private static readonly Guid BaseCurrency = Guid.Parse("70000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(100, 102.5, 12.3456, 30.864)]
    [InlineData(100, 97.5, 12.3456, 30.864)]
    [InlineData(40, 39.999, 8.75, 0.00875)]
    [InlineData(0.001, 0.002, 0.0001, 0.0000001)]
    public void A_difference_is_valued_at_the_position_average_exactly_and_the_average_is_unchanged(
        decimal book, decimal counted, decimal average, decimal amount)
    {
        var position = AverageCostPosition.Known(500m, average);

        var cost = StockCountCostCalculator.For(Count(book, counted), position);

        Assert.Equal((StockCountCostStatus.Known, (decimal?)average, (decimal?)amount, (AverageCostUnreadableReason?)null),
            (cost.Status, cost.AverageCostPerKg, cost.Amount, cost.UnreadableReason));
        Assert.Equal(AverageCostPosition.Known(500m, average), position);
    }

    [Fact]
    public void A_surplus_on_a_position_at_zero_stock_uses_its_last_average()
    {
        // SAT-D331 (2) with SAT-D296: the position fell to exactly 0 kg and kept its last average.
        var cost = StockCountCostCalculator.For(Count(0m, 1.25m), AverageCostPosition.Known(0m, 10.5m));

        Assert.Equal((StockCountCostStatus.Known, (decimal?)10.5m, (decimal?)13.125m),
            (cost.Status, cost.AverageCostPerKg, cost.Amount));
    }

    [Theory]
    [InlineData(0, 7.25)]
    [InlineData(100, 100.5)]
    public void Every_surplus_without_a_readable_average_is_added_with_its_cost_not_calculated(
        decimal book, decimal counted)
    {
        // SAT-D331 (3)-(4): unreadable or never recorded, a shown-at-zero label or not: no guessed figure, never zero.
        var unreadable = StockCountCostCalculator.For(Count(book, counted),
            AverageCostPosition.Unreadable(AverageCostUnreadableReason.LedgerStockMismatch));
        var noSource = StockCountCostCalculator.For(Count(book, counted), null);

        Assert.Equal((StockCountCostStatus.NotCalculated, (decimal?)null, (decimal?)null,
                (AverageCostUnreadableReason?)AverageCostUnreadableReason.LedgerStockMismatch),
            (unreadable.Status, unreadable.AverageCostPerKg, unreadable.Amount, unreadable.UnreadableReason));
        Assert.Equal((StockCountCostStatus.NotCalculated, (decimal?)null, (AverageCostUnreadableReason?)null),
            (noSource.Status, noSource.Amount, noSource.UnreadableReason));
        Assert.Same(CostResult.NotCalculated, unreadable.ToMovementCost(BaseCurrency));
        Assert.Same(CostResult.NotCalculated, noSource.ToMovementCost(BaseCurrency));
    }

    [Fact]
    public void A_shortage_without_a_readable_average_is_refused_and_has_no_movement_cost()
    {
        // SAT-D334 (2): "hesaplanamadı" and the deduction stops.
        var unreadable = StockCountCostCalculator.For(Count(100m, 96m),
            AverageCostPosition.Unreadable(AverageCostUnreadableReason.NegativeStock));
        var noSource = StockCountCostCalculator.For(Count(100m, 96m), null);

        Assert.Equal((StockCountCostStatus.Refused, (decimal?)null,
                (AverageCostUnreadableReason?)AverageCostUnreadableReason.NegativeStock),
            (unreadable.Status, unreadable.Amount, unreadable.UnreadableReason));
        Assert.Equal((StockCountCostStatus.Refused, (decimal?)null), (noSource.Status, noSource.Amount));
        Assert.Equal(InventoryRuleCodes.InvalidStockCount,
            Assert.Throws<InventoryRuleException>(() => unreadable.ToMovementCost(BaseCurrency)).Code);
        Assert.Equal(InventoryRuleCodes.InvalidStockCount,
            Assert.Throws<InventoryRuleException>(() => noSource.ToMovementCost(BaseCurrency)).Code);
    }

    [Fact]
    public void A_known_cost_becomes_a_known_movement_cost_in_the_base_currency()
    {
        var cost = StockCountCostCalculator.For(Count(10m, 12m), AverageCostPosition.Known(10m, 3.3333m));

        var movementCost = cost.ToMovementCost(BaseCurrency);

        Assert.Equal((CostStatus.Known, (decimal?)6.6666m, (Guid?)BaseCurrency),
            (movementCost.Status, movementCost.Amount, movementCost.CurrencyId));
    }

    [Fact]
    public void A_count_without_a_difference_has_no_cost_effect()
    {
        Assert.Equal(InventoryRuleCodes.InvalidStockCount, Assert.Throws<InventoryRuleException>(() =>
            StockCountCostCalculator.For(Count(10m, 10m), AverageCostPosition.Known(10m, 1m))).Code);
        Assert.Throws<ArgumentNullException>(() => StockCountCostCalculator.For(null!, null));
    }

    private static StockCount Count(decimal book, decimal counted) =>
        new(Guid.NewGuid(), CompanyA, Position(), Kilograms.Of(book), Kilograms.Of(counted), At, "Sayım farkı");
}
