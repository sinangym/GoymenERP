using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// MFG-4 — closing with work in progress (Business #66 5728619644 and 5730092974, #299 5730573197,
/// on DEC-PRDCT-006/007 and Master v3 §28.9). Every parent roll reconciles on its own:
/// issued − returned = good + second quality + usable remnant + scrap + process loss + remaining
/// WIP + unexplained. Remaining WIP does not block the close; any non-zero unexplained difference
/// does (no Ö7 tolerance). WIP is never zeroed, scrapped or turned into output, never assumed when
/// unknown, and never borrowed from another roll.
/// </summary>
public sealed class WipCloseTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000c001");
    private static readonly Guid RollA = Guid.Parse("30000000-0000-0000-0000-00000000a001");
    private static readonly Guid RollB = Guid.Parse("30000000-0000-0000-0000-00000000a002");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000b001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000b002");

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionOrder Order(params Guid[] rolls)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            (rolls.Length == 0 ? new[] { RollA } : rolls).Select(roll => new PlannedInput(roll, Kg(10_000m))));
        plan.Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    private static string CloseRefused(ProductionOrder order) =>
        Assert.Throws<ManufacturingRuleException>(() => order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch)).Code;

    [Fact]
    public void Remaining_WIP_bound_to_its_roll_with_no_unexplained_difference_closes_and_stays_WIP()
    {
        var order = Order();
        order.Issue(RollA, Kg(10_000m));
        order.Declare(RollA, OutputClass.GoodProduct, Kg(3_000m));
        order.Declare(RollA, OutputClass.ProcessLoss, Kg(50m));
        order.MeasureWip(RollA, Kg(6_950m));

        order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch);

        Assert.Equal(OrderStatus.Closed, order.Status);
        // The remaining WIP is kept as its own position: not zeroed, not scrap, not good output.
        var balance = order.BalanceOf(RollA);
        Assert.Equal(6_950m, balance.WorkInProgress.Value);
        Assert.Equal(3_000m, balance.OutputOf(OutputClass.GoodProduct).Value);
        Assert.Equal(0m, balance.OutputOf(OutputClass.Scrap).Value);
        Assert.Equal(50m, balance.OutputOf(OutputClass.ProcessLoss).Value);
        Assert.Equal(0m, balance.Unexplained);
        Assert.Single(order.WipMeasurements);
    }

    [Fact]
    public void A_closed_order_takes_no_new_realization_so_its_WIP_cannot_be_rewritten()
    {
        var order = Order();
        order.Issue(RollA, Kg(10_000m));
        order.Declare(RollA, OutputClass.GoodProduct, Kg(4_000m));
        order.MeasureWip(RollA, Kg(6_000m));
        order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch);

        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.Declare(RollA, OutputClass.GoodProduct, Kg(6_000m))).Code);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.Declare(RollA, OutputClass.Scrap, Kg(6_000m))).Code);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.MeasureWip(RollA, Kilograms.Zero)).Code);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.Return(RollA, Kg(1m))).Code);
        Assert.Equal(ManufacturingRuleCodes.OrderClosed, Assert.Throws<ManufacturingRuleException>(
            () => order.Issue(RollA, Kg(1m))).Code);

        Assert.Equal(6_000m, order.BalanceOf(RollA).WorkInProgress.Value);
        Assert.Equal(4_000m, order.BalanceOf(RollA).OutputOf(OutputClass.GoodProduct).Value);
    }

    [Fact]
    public void Writing_WIP_down_to_zero_without_declaring_or_returning_it_is_an_unexplained_difference()
    {
        var order = Order();
        order.Issue(RollA, Kg(10_000m));
        order.Declare(RollA, OutputClass.GoodProduct, Kg(3_000m));
        order.MeasureWip(RollA, Kg(7_000m));
        order.MeasureWip(RollA, Kilograms.Zero);

        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, CloseRefused(order));
        Assert.Equal(7_000m, order.BalanceOf(RollA).Unexplained);
        Assert.Equal(OrderStatus.Open, order.Status);
    }

    [Fact]
    public void Unknown_WIP_is_never_assumed_zero_mass_that_nobody_measured_or_declared_refuses_the_close()
    {
        // No WIP reading at all: the mass still in process is not taken as 0 kg of WIP; it stays an
        // unexplained difference and the close is fail-closed.
        var order = Order();
        order.Issue(RollA, Kg(10_000m));
        order.Declare(RollA, OutputClass.GoodProduct, Kg(3_000m));

        Assert.Empty(order.WipMeasurements);
        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, CloseRefused(order));
        Assert.Equal(7_000m, order.BalanceOf(RollA).Unexplained);
        Assert.Equal(OrderStatus.Open, order.Status);
    }

    [Fact]
    public void Unbound_WIP_cannot_be_recorded_so_it_can_never_account_for_a_roll()
    {
        var order = Order(RollA);
        order.Issue(RollA, Kg(10_000m));
        order.Declare(RollA, OutputClass.GoodProduct, Kg(3_000m));

        // A roll outside the order, and a planned roll nothing was issued from, carry no WIP.
        Assert.Equal(ManufacturingRuleCodes.RollNotInOrder, Assert.Throws<ManufacturingRuleException>(
            () => order.MeasureWip(RollB, Kg(7_000m))).Code);
        var twoRolls = Order(RollA, RollB);
        twoRolls.Issue(RollA, Kg(10_000m));
        Assert.Equal(ManufacturingRuleCodes.RollNotIssued, Assert.Throws<ManufacturingRuleException>(
            () => twoRolls.MeasureWip(RollB, Kg(7_000m))).Code);

        Assert.Empty(order.WipMeasurements);
        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, CloseRefused(order));
    }

    [Fact]
    public void Rolls_never_offset_each_other_a_surplus_on_one_roll_does_not_cover_a_gap_on_another()
    {
        // Totals net to zero across the order, but each roll is off by 1.000 kg: refused.
        var order = Order(RollA, RollB);
        order.Issue(RollA, Kg(5_000m));
        order.Issue(RollB, Kg(5_000m));
        order.Declare(RollA, OutputClass.GoodProduct, Kg(3_000m));
        order.MeasureWip(RollA, Kg(3_000m));
        order.Declare(RollB, OutputClass.GoodProduct, Kg(4_000m));

        Assert.Equal(-1_000m, order.BalanceOf(RollA).Unexplained);
        Assert.Equal(1_000m, order.BalanceOf(RollB).Unexplained);
        var error = Assert.Throws<ManufacturingRuleException>(() => order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch));
        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, error.Code);
        Assert.Contains(RollA.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains(RollB.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Equal(OrderStatus.Open, order.Status);

        // Each roll reconciled on its own, the same order closes with its WIP kept.
        order.Declare(RollB, OutputClass.Scrap, Kg(1_000m));
        order.MeasureWip(RollA, Kg(2_000m));
        order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch);
        Assert.Equal((OrderStatus.Closed, 2_000m), (order.Status, order.BalanceOf(RollA).WorkInProgress.Value));
    }
}
