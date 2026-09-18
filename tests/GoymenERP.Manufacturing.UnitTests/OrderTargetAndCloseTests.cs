using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// The two MFG-1 decisions (coordinator, 2026-09-18): the remaining target is measured only in
/// the unit the plan DECLARES, otherwise it is unavailable; and with no approved Ö7 tolerance,
/// an order with any unexplained difference does not close.
/// </summary>
public sealed class OrderTargetAndCloseTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000c001");
    private static readonly Guid MasterRoll = Guid.Parse("30000000-0000-0000-0000-00000000a001");
    private static readonly Guid OtherRoll = Guid.Parse("30000000-0000-0000-0000-00000000a002");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000b001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000b002");

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionOrder Order(ProductionTarget? target, params PlannedInput[] inputs)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            inputs.Length == 0 ? new[] { new PlannedInput(MasterRoll, Kg(10_000m)) } : inputs, target);
        plan.Approve(Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    [Fact]
    public void Without_a_declared_target_the_remaining_target_is_unavailable_not_guessed()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));

        var remaining = order.RemainingTargetOf();

        Assert.False(remaining.IsAvailable);
        Assert.Null(remaining.Basis);
        Assert.Null(remaining.Target);
        Assert.Null(remaining.Achieved);
        Assert.Null(remaining.Remaining);
    }

    [Fact]
    public void An_input_mass_target_counts_input_issued_net_of_returns()
    {
        var order = Order(new ProductionTarget(TargetBasis.InputMass, Kg(10_000m)));
        order.Issue(MasterRoll, Kg(6_000m));
        order.Return(MasterRoll, Kg(1_000m));
        // Output does not count toward an input target.
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(4_000m));

        var remaining = order.RemainingTargetOf();

        Assert.True(remaining.IsAvailable);
        Assert.Equal(TargetBasis.InputMass, remaining.Basis);
        Assert.Equal(5_000m, remaining.Achieved!.Value.Value);
        Assert.Equal(5_000m, remaining.Remaining);
    }

    [Fact]
    public void A_good_output_target_counts_good_product_only()
    {
        var order = Order(new ProductionTarget(TargetBasis.GoodOutputMass, Kg(9_700m)));
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(4_850m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(200m));
        order.Declare(MasterRoll, OutputClass.SecondQuality, Kg(50m));

        Assert.Equal(4_850m, order.RemainingTargetOf().Remaining);

        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(4_850m));
        Assert.Equal(0m, order.RemainingTargetOf().Remaining);
    }

    [Fact]
    public void An_exceeded_target_is_reported_with_its_sign()
    {
        var order = Order(new ProductionTarget(TargetBasis.GoodOutputMass, Kg(9_000m)));
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));

        Assert.Equal(-700m, order.RemainingTargetOf().Remaining);
    }

    [Fact]
    public void An_order_with_an_unexplained_difference_does_not_close_and_keeps_it_visible()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(200m));

        var error = Assert.Throws<ManufacturingRuleException>(() => order.Close(Closer, DateTimeOffset.UnixEpoch));

        Assert.Equal("unexplained_difference", error.Code);
        Assert.Equal(OrderStatus.Open, order.Status);
        Assert.Null(order.ClosedByUserId);
        // Still unexplained, still not written off as loss.
        Assert.Equal(100m, order.BalanceOf(MasterRoll).Unexplained);
        Assert.Equal(0m, order.BalanceOf(MasterRoll).OutputOf(OutputClass.ProcessLoss).Value);
    }

    [Fact]
    public void One_unbalanced_roll_keeps_the_whole_order_open()
    {
        var order = Order(null, new PlannedInput(MasterRoll, Kg(10_000m)), new PlannedInput(OtherRoll, Kg(5_000m)));
        order.Issue(MasterRoll, Kg(10_000m));
        order.Issue(OtherRoll, Kg(5_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(10_000m));
        order.Declare(OtherRoll, OutputClass.GoodProduct, Kg(4_999m));

        var error = Assert.Throws<ManufacturingRuleException>(() => order.Close(Closer, DateTimeOffset.UnixEpoch));
        Assert.Equal("unexplained_difference", error.Code);
        Assert.Contains(OtherRoll.ToString(), error.Message);
    }

    [Fact]
    public void A_fully_accounted_order_closes_and_then_takes_no_more_realization()
    {
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(200m));
        order.Declare(MasterRoll, OutputClass.ProcessLoss, Kg(100m));

        order.Close(Closer, DateTimeOffset.UnixEpoch);

        Assert.Equal(OrderStatus.Closed, order.Status);
        Assert.Equal(Closer, order.ClosedByUserId);
        Assert.Equal("order_closed", Assert.Throws<ManufacturingRuleException>(
            () => order.Issue(MasterRoll, Kg(1m))).Code);
        Assert.Equal("order_closed", Assert.Throws<ManufacturingRuleException>(
            () => order.Declare(MasterRoll, OutputClass.Scrap, Kg(1m))).Code);
        Assert.Equal("order_closed", Assert.Throws<ManufacturingRuleException>(
            () => order.Return(MasterRoll, Kg(1m))).Code);
        Assert.Equal("order_closed", Assert.Throws<ManufacturingRuleException>(
            () => order.MeasureWip(MasterRoll, Kg(1m))).Code);
        Assert.Equal("order_closed", Assert.Throws<ManufacturingRuleException>(
            () => order.Close(Closer, DateTimeOffset.UnixEpoch)).Code);
    }

    [Fact]
    public void An_order_with_mass_still_in_process_does_not_close_until_it_is_declared_or_returned()
    {
        // TEMPORARY rule, awaiting Business confirmation (see ProductionOrder.Close).
        var order = Order(target: null);
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(3_000m));
        order.MeasureWip(MasterRoll, Kg(7_000m));
        Assert.True(order.BalanceOf(MasterRoll).IsFullyAccounted);

        var wipOpen = Assert.Throws<ManufacturingRuleException>(() => order.Close(Closer, DateTimeOffset.UnixEpoch));
        Assert.Equal("wip_open", wipOpen.Code);
        Assert.Equal(OrderStatus.Open, order.Status);
        Assert.Equal(7_000m, order.BalanceOf(MasterRoll).WorkInProgress.Value);

        // Writing the WIP down to zero without declaring or returning it does not close the
        // order: the 7.000 kg reappears as an unexplained difference.
        order.MeasureWip(MasterRoll, Kilograms.Zero);
        Assert.Equal("unexplained_difference", Assert.Throws<ManufacturingRuleException>(
            () => order.Close(Closer, DateTimeOffset.UnixEpoch)).Code);

        // Declared and returned, the mass is accounted for and the order closes.
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(5_000m));
        order.Return(MasterRoll, Kg(2_000m));
        order.Close(Closer, DateTimeOffset.UnixEpoch);
        Assert.Equal(OrderStatus.Closed, order.Status);
    }

    [Fact]
    public void The_order_keeps_the_target_it_was_created_with()
    {
        var target = new ProductionTarget(TargetBasis.InputMass, Kg(10_000m));
        var order = Order(target);
        Assert.Equal(target, order.Target);
    }
}
