using GoymenERP.Modules.Inventory.Domain.Shortages;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D42 (STK-S31): the one dated-shortage calculator. Rule 1 (net open sales kg above free stock plus open purchase kg is
/// a shortage), rule 2 (a purchase dated after the line's due date is "geç karşılanıyor"), reservation netting, undated lines
/// and undated purchases. Pure figures, SENTETIK ids.
/// </summary>
public sealed class ShortageWarningCalculatorTests
{
    private static readonly Guid Product = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid OrderA = Guid.Parse("00000000-0000-4000-8000-000000000002");
    private static readonly Guid OrderB = Guid.Parse("00000000-0000-4000-8000-000000000003");
    private static readonly Guid LineA = Guid.Parse("00000000-0000-4000-8000-000000000004");
    private static readonly Guid LineB = Guid.Parse("00000000-0000-4000-8000-000000000005");
    private static readonly Guid Purchase = Guid.Parse("00000000-0000-4000-8000-000000000006");
    private static readonly Guid PurchaseLine = Guid.Parse("00000000-0000-4000-8000-000000000007");
    private static readonly Guid PurchaseLine2 = Guid.Parse("00000000-0000-4000-8000-000000000008");

    [Fact]
    public void Net_open_sales_above_free_stock_plus_open_purchases_is_a_shortage_of_the_difference()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 100m,
            [Demand(LineA, "SENTETIK-SO-1", new DateOnly(2026, 12, 1), 300m)],
            [Supply(PurchaseLine, new DateOnly(2026, 11, 20), 150m)]));

        Assert.Equal(ShortageProductStatus.Shortage, result.Status);
        Assert.Equal(300m, result.OpenDemandKg);
        Assert.Equal(300m, result.NetDemandKg);
        Assert.Equal(100m, result.FreeStockKg);
        Assert.Equal(150m, result.OpenSupplyKg);
        Assert.Equal(50m, result.ShortageKg);
        var line = Assert.Single(result.Lines);
        Assert.Equal(100m, line.FromStockKg);
        Assert.Equal(150m, line.FromSupplyKg);
        Assert.Equal(50m, line.UncoveredKg);
        Assert.Equal(ShortageLineCoverage.Uncovered, line.Coverage);
    }

    [Fact]
    public void Demand_equal_to_free_stock_plus_open_purchases_is_not_a_shortage()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 100m,
            [Demand(LineA, "SENTETIK-SO-1", new DateOnly(2026, 12, 1), 250m)],
            [Supply(PurchaseLine, new DateOnly(2026, 11, 20), 150m)]));

        Assert.Equal(ShortageProductStatus.Covered, result.Status);
        Assert.Equal(0m, result.ShortageKg);
        Assert.Equal(ShortageLineCoverage.Covered, Assert.Single(result.Lines).Coverage);
    }

    [Fact]
    public void A_purchase_dated_after_the_due_date_marks_the_line_late_covered()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 0m,
            [Demand(LineA, "SENTETIK-SO-1", new DateOnly(2026, 11, 20), 80m)],
            [Supply(PurchaseLine, new DateOnly(2026, 11, 25), 100m)]));

        Assert.Equal(ShortageProductStatus.LateCovered, result.Status);
        Assert.Equal(0m, result.ShortageKg);
        var line = Assert.Single(result.Lines);
        Assert.Equal(ShortageLineCoverage.LateCovered, line.Coverage);
        Assert.Equal(new DateOnly(2026, 11, 25), line.LatestSupplyDate);
    }

    [Fact]
    public void Earliest_due_takes_free_stock_first_so_only_the_later_line_waits_for_the_purchase()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 50m,
            [
                Demand(LineB, "SENTETIK-SO-2", new DateOnly(2026, 12, 10), 40m, OrderB),
                Demand(LineA, "SENTETIK-SO-1", new DateOnly(2026, 11, 20), 50m),
            ],
            [Supply(PurchaseLine, new DateOnly(2026, 12, 5), 40m)]));

        Assert.Equal(ShortageProductStatus.Covered, result.Status);
        Assert.Collection(result.Lines,
            first =>
            {
                Assert.Equal(LineA, first.Line.LineId);
                Assert.Equal(50m, first.FromStockKg);
                Assert.Equal(ShortageLineCoverage.Covered, first.Coverage);
            },
            second =>
            {
                Assert.Equal(LineB, second.Line.LineId);
                Assert.Equal(40m, second.FromSupplyKg);
                Assert.Equal(ShortageLineCoverage.Covered, second.Coverage);
            });
        Assert.Equal(new DateOnly(2026, 11, 20), result.EarliestDueDate);
    }

    [Fact]
    public void A_line_s_own_reservation_is_netted_and_never_counted_as_free_stock()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 0m,
            [Demand(LineA, "SENTETIK-SO-1", new DateOnly(2026, 12, 1), 120m, reservedKg: 120m)],
            []));

        Assert.Equal(ShortageProductStatus.Covered, result.Status);
        Assert.Equal(120m, result.ReservedKg);
        Assert.Equal(0m, result.NetDemandKg);
    }

    [Fact]
    public void A_reservation_above_the_open_quantity_nets_only_the_open_quantity()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 0m,
            [Demand(LineA, "SENTETIK-SO-1", null, 30m, reservedKg: 45m)],
            []));

        Assert.Equal(30m, result.ReservedKg);
        Assert.Equal(0m, result.NetDemandKg);
        Assert.Equal(0m, result.ShortageKg);
    }

    [Fact]
    public void An_undated_purchase_never_says_on_time_it_says_the_date_is_unknown()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 10m,
            [Demand(LineA, "SENTETIK-SO-1", new DateOnly(2026, 12, 1), 60m)],
            [Supply(PurchaseLine, null, 100m)]));

        Assert.Equal(ShortageProductStatus.CoverageDateUnknown, result.Status);
        var line = Assert.Single(result.Lines);
        Assert.Equal(ShortageLineCoverage.CoverageDateUnknown, line.Coverage);
        Assert.Null(line.LatestSupplyDate);
    }

    [Fact]
    public void A_known_late_purchase_outranks_an_undated_one_on_the_same_line()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 0m,
            [Demand(LineA, "SENTETIK-SO-1", new DateOnly(2026, 12, 1), 60m)],
            [Supply(PurchaseLine, new DateOnly(2026, 12, 3), 30m), Supply(PurchaseLine2, null, 30m)]));

        Assert.Equal(ShortageLineCoverage.LateCovered, Assert.Single(result.Lines).Coverage);
    }

    [Fact]
    public void A_line_without_a_due_date_is_never_late_and_is_matched_last()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 20m,
            [
                Demand(LineB, "SENTETIK-SO-0", null, 20m, OrderB),
                Demand(LineA, "SENTETIK-SO-1", new DateOnly(2026, 12, 1), 20m),
            ],
            [Supply(PurchaseLine, new DateOnly(2027, 1, 15), 20m)]));

        Assert.Equal(LineA, result.Lines[0].Line.LineId);
        Assert.Equal(ShortageLineCoverage.Covered, result.Lines[0].Coverage);
        Assert.Equal(ShortageLineCoverage.Covered, result.Lines[1].Coverage);
        Assert.Equal(20m, result.Lines[1].FromSupplyKg);
    }

    [Fact]
    public void Kilograms_stay_exact_without_rounding()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 0.001m,
            [Demand(LineA, "SENTETIK-SO-1", null, 0.003m)],
            [Supply(PurchaseLine, null, 0.001m)]));

        Assert.Equal(0.001m, result.ShortageKg);
    }

    [Fact]
    public void No_demand_is_covered_with_zero_figures()
    {
        var result = ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 5m, [], []));

        Assert.Equal(ShortageProductStatus.Covered, result.Status);
        Assert.Empty(result.Lines);
        Assert.Null(result.EarliestDueDate);
    }

    [Fact]
    public void Negative_kilograms_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, -1m, [], [])));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 0m,
                [Demand(LineA, "SENTETIK-SO-1", null, -1m)], [])));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShortageWarningCalculator.Calculate(new ShortageProductInput(Product, 0m, [],
                [Supply(PurchaseLine, null, -1m)])));
    }

    private static ShortageDemandLine Demand(Guid lineId, string documentNo, DateOnly? dueDate, decimal openKg,
        Guid? orderId = null, decimal reservedKg = 0m) =>
        new(orderId ?? OrderA, lineId, documentNo, 1, dueDate, openKg, reservedKg);

    private static ShortageSupplyLine Supply(Guid lineId, DateOnly? expectedDate, decimal remainingKg) =>
        new(Purchase, lineId, expectedDate, remainingKg);
}
