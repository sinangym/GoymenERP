using GoymenERP.Modules.Inventory.Domain.Costing;
using static GoymenERP.Modules.Inventory.Domain.Costing.AverageCostUnreadableReason;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D272 / SAT-D347 / SAT-D350 (STK-S35): the company-wide (all locations) average is Inventory's own deterministic
/// aggregation. The weight is each location's on-hand kilograms, a location without stock is left out, one unreadable
/// location with stock makes the whole answer unreadable (no partial average), and the figure is rounded once at the
/// 4-decimal internal scale, AwayFromZero (SAT-D265 (2)). SENTETIK data only.
/// </summary>
public sealed class InventoryCompanyWideAverageCostCalculatorTests
{
    private static readonly Guid First = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-4000-8000-000000000002");
    private static readonly Guid Third = Guid.Parse("00000000-0000-4000-8000-000000000003");

    private static CompanyWideAverageCostLocation Known(Guid location, decimal kg, decimal average) =>
        new(location, kg, AverageCostPosition.Known(kg, average));

    private static CompanyWideAverageCostLocation Unreadable(Guid location, decimal kg, AverageCostUnreadableReason reason) =>
        new(location, kg, AverageCostPosition.Unreadable(reason));

    [Fact]
    public void No_location_with_stock_is_no_source_with_no_figure()
    {
        var result = CompanyWideAverageCostCalculator.Aggregate([]);

        Assert.Equal((CompanyWideAverageCostStatus.NoSource, null, null, null, 0m),
            (result.Status, result.AverageCostPerKg, result.UnreadableReason, result.UnreadableLocationId, result.OnHandKg));
        Assert.Empty(result.Included);
    }

    [Fact]
    public void A_location_without_stock_is_left_out_of_the_weight_whatever_its_ledger_says()
    {
        var result = CompanyWideAverageCostCalculator.Aggregate(
        [
            new CompanyWideAverageCostLocation(First, 0m, AverageCostPosition.Known(0m, 99m)),
            new CompanyWideAverageCostLocation(Second, 0m, AverageCostPosition.Unreadable(NegativeStock)),
            new CompanyWideAverageCostLocation(Third, 0m, null),
        ]);

        Assert.Equal(CompanyWideAverageCostStatus.NoSource, result.Status);
        Assert.Empty(result.Included);

        var withStock = CompanyWideAverageCostCalculator.Aggregate(
        [
            new CompanyWideAverageCostLocation(First, 0m, AverageCostPosition.Unreadable(NegativeStock)),
            Known(Second, 40m, 12.5m),
        ]);

        Assert.Equal((CompanyWideAverageCostStatus.Known, 12.5m, 40m),
            (withStock.Status, withStock.AverageCostPerKg, withStock.OnHandKg));
        Assert.Equal([Second], withStock.Included.Select(location => location.LocationId));
    }

    [Fact]
    public void The_average_is_weighted_by_each_locations_on_hand_kilograms()
    {
        var result = CompanyWideAverageCostCalculator.Aggregate([Known(Second, 20m, 13m), Known(First, 10m, 10m)]);

        Assert.Equal((CompanyWideAverageCostStatus.Known, 12.0000m, 30m, null, null),
            (result.Status, result.AverageCostPerKg, result.OnHandKg, result.UnreadableReason, result.UnreadableLocationId));
        Assert.Equal([First, Second], result.Included.Select(location => location.LocationId));
    }

    [Fact]
    public void The_figure_is_rounded_once_to_four_decimals_away_from_zero()
    {
        // (3·10 + 4·11) / 7 = 10.571428…
        Assert.Equal(10.5714m,
            CompanyWideAverageCostCalculator.Aggregate([Known(First, 3m, 10m), Known(Second, 4m, 11m)]).AverageCostPerKg);

        // (1·10.0002 + 1·10.0003) / 2 = 10.00025: AwayFromZero gives 10.0003 (banker's rounding would give 10.0002).
        Assert.Equal(10.0003m,
            CompanyWideAverageCostCalculator.Aggregate([Known(First, 1m, 10.0002m), Known(Second, 1m, 10.0003m)]).AverageCostPerKg);

        // Fractional kilograms weigh as they are: (0.125·8 + 0.375·16) / 0.5 = 14.
        Assert.Equal(14.0000m,
            CompanyWideAverageCostCalculator.Aggregate([Known(First, 0.125m, 8m), Known(Second, 0.375m, 16m)]).AverageCostPerKg);
    }

    [Fact]
    public void One_location_is_its_own_average()
    {
        var result = CompanyWideAverageCostCalculator.Aggregate([Known(First, 7.5m, 11.1111m)]);

        Assert.Equal((CompanyWideAverageCostStatus.Known, 11.1111m, 7.5m), (result.Status, result.AverageCostPerKg, result.OnHandKg));
    }

    [Fact]
    public void One_unreadable_location_with_stock_makes_the_whole_answer_unreadable_with_no_partial_average()
    {
        var result = CompanyWideAverageCostCalculator.Aggregate(
            [Known(First, 100m, 10m), Unreadable(Second, 1m, ForeignCurrency), Known(Third, 100m, 12m)]);

        Assert.Equal((CompanyWideAverageCostStatus.Unreadable, null, ForeignCurrency, Second, 201m),
            (result.Status, result.AverageCostPerKg, result.UnreadableReason, result.UnreadableLocationId, result.OnHandKg));
        Assert.Equal([First, Second, Third], result.Included.Select(location => location.LocationId));
    }

    [Fact]
    public void Several_unreadable_locations_are_named_by_the_first_in_location_order_whatever_the_input_order()
    {
        var result = CompanyWideAverageCostCalculator.Aggregate(
            [Unreadable(Third, 5m, ZeroCostReceipt), Known(First, 5m, 10m), Unreadable(Second, 5m, ProductionSource)]);

        Assert.Equal((CompanyWideAverageCostStatus.Unreadable, ProductionSource, Second),
            (result.Status, result.UnreadableReason, result.UnreadableLocationId));
    }

    [Fact]
    public void Stock_the_ledger_never_saw_or_costed_for_another_quantity_reads_as_uncosted_stock()
    {
        var missing = CompanyWideAverageCostCalculator.Aggregate(
            [Known(First, 10m, 10m), new CompanyWideAverageCostLocation(Second, 4m, null)]);
        Assert.Equal((CompanyWideAverageCostStatus.Unreadable, null, LedgerStockMismatch, Second),
            (missing.Status, missing.AverageCostPerKg, missing.UnreadableReason, missing.UnreadableLocationId));

        var moreStock = CompanyWideAverageCostCalculator.Aggregate(
            [new CompanyWideAverageCostLocation(First, 12m, AverageCostPosition.Known(10m, 10m))]);
        Assert.Equal((CompanyWideAverageCostStatus.Unreadable, LedgerStockMismatch, First),
            (moreStock.Status, moreStock.UnreadableReason, moreStock.UnreadableLocationId));

        var lessStock = CompanyWideAverageCostCalculator.Aggregate(
            [new CompanyWideAverageCostLocation(First, 8m, AverageCostPosition.Known(10m, 10m))]);
        Assert.Equal((CompanyWideAverageCostStatus.Unreadable, LedgerStockMismatch, First),
            (lessStock.Status, lessStock.UnreadableReason, lessStock.UnreadableLocationId));
    }

    [Fact]
    public void Malformed_input_is_refused_as_a_caller_fault()
    {
        Assert.Throws<ArgumentNullException>(() => CompanyWideAverageCostCalculator.Aggregate(null!));
        Assert.Throws<ArgumentNullException>(() => CompanyWideAverageCostCalculator.Aggregate([null!]));
        Assert.Throws<ArgumentException>(() => CompanyWideAverageCostCalculator.Aggregate([Known(Guid.Empty, 1m, 1m)]));
        Assert.Throws<ArgumentException>(() => CompanyWideAverageCostCalculator.Aggregate([Known(First, 1m, 1m), Known(First, 2m, 1m)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanyWideAverageCostCalculator.Aggregate(
            [new CompanyWideAverageCostLocation(First, -1m, null)]));
    }
}
