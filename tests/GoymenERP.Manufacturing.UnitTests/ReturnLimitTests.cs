using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// F-009 D4 (analysis §2, §4.4): a return is bounded not only by the issued kg but by the input still unconsumed —
/// issued minus returned minus the net output (declarations net of their reversals) minus the latest WIP reading. A
/// roll-based process measures it on the parent roll, a process without a main roll input (paint) on the order total
/// (URT-D49). The input of a roll whose outputs still stand cannot be sent back; the outputs are reversed or the WIP
/// re-measured first. A refused return changes nothing; a stored return replays under the issued bound only.
/// </summary>
public sealed class ReturnLimitTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000d004");
    private static readonly Guid RollA = Guid.Parse("30000000-0000-0000-0000-00000000d401");
    private static readonly Guid RollB = Guid.Parse("30000000-0000-0000-0000-00000000d402");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000d401");
    private const string Reason = "SENTETIK tartım hatası";

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionOrder Order(ManufacturingProcess process, params Guid[] rolls)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, process,
            rolls.Select(roll => new PlannedInput(roll, Kg(10_000m))));
        plan.WithDueDate().Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    /// <summary>A good product bound to its class line (a plan without output lines), so it can be reversed.</summary>
    private static OutputDeclaration DeclareGood(ProductionOrder order, Guid roll, decimal mass) =>
        order.DeclareOutput(order.Version, Guid.NewGuid(), roll, OutputClass.GoodProduct, Kg(mass), OrderSteps.Next(),
            PlanOutputLineReference.ClassLine);

    private static string Refused(ProductionOrder order, Guid roll, decimal mass) =>
        Assert.Throws<ManufacturingRuleException>(() => order.Return(roll, Kg(mass))).Code;

    [Fact]
    public void A_return_above_the_issued_kg_keeps_the_issued_code_before_the_unconsumed_bound()
    {
        var order = Order(ManufacturingProcess.Slitting, RollA);
        order.Issue(RollA, Kg(1_000m));
        DeclareGood(order, RollA, 600m);

        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsIssued, Refused(order, RollA, 1_000.001m));
    }

    [Fact]
    public void A_return_above_issued_minus_returned_minus_net_output_is_refused_and_changes_nothing()
    {
        var order = Order(ManufacturingProcess.Slitting, RollA);
        order.Issue(RollA, Kg(1_000m));
        order.Return(RollA, Kg(100m));
        DeclareGood(order, RollA, 600m);
        var versionBefore = order.Version;

        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollA, 300.001m));

        Assert.Equal(versionBefore, order.Version);
        Assert.Single(order.Returns);
        Assert.Equal(300m, order.BalanceOf(RollA).Unexplained);
    }

    [Fact]
    public void A_return_of_exactly_the_unconsumed_input_is_recorded_and_leaves_nothing_more_to_return()
    {
        var order = Order(ManufacturingProcess.Slitting, RollA);
        order.Issue(RollA, Kg(1_000m));
        DeclareGood(order, RollA, 600m);

        order.Return(RollA, Kg(400m));

        Assert.Equal((400m, 0m), (order.BalanceOf(RollA).Returned.Value, order.BalanceOf(RollA).Unexplained));
        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollA, 0.001m));
    }

    [Fact]
    public void The_input_of_a_fully_consumed_roll_is_returned_only_after_its_output_is_reversed()
    {
        var order = Order(ManufacturingProcess.Slitting, RollA);
        order.Issue(RollA, Kg(1_000m));
        var declaration = DeclareGood(order, RollA, 1_000m);

        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollA, 1m));

        order.ReverseOutput(order.Version, Guid.NewGuid(), declaration.OutputId, Kg(250m), Reason, OrderSteps.Next());
        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollA, 250.001m));
        order.Return(RollA, Kg(250m));

        Assert.Equal((750m, 250m, 0m),
            (order.NetMassOf(declaration.OutputId).Value, order.BalanceOf(RollA).Returned.Value,
                order.BalanceOf(RollA).Unexplained));
    }

    [Fact]
    public void Every_output_class_and_the_latest_WIP_reading_lower_the_bound_and_a_new_reading_releases_it()
    {
        var order = Order(ManufacturingProcess.Slitting, RollA);
        order.Issue(RollA, Kg(1_000m));
        order.Declare(RollA, OutputClass.Scrap, Kg(50m));
        order.Declare(RollA, OutputClass.UsableRemnant, Kg(50m));
        DeclareGood(order, RollA, 500m);
        order.MeasureWip(RollA, Kg(300m));

        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollA, 100.001m));

        order.MeasureWip(RollA, Kg(0m));
        order.Return(RollA, Kg(400m));

        Assert.Equal(0m, order.BalanceOf(RollA).Unexplained);
    }

    [Fact]
    public void A_roll_based_order_bounds_each_roll_on_its_own_and_never_borrows_another_rolls_input()
    {
        var order = Order(ManufacturingProcess.Slitting, RollA, RollB);
        order.Issue(RollA, Kg(1_000m));
        order.Issue(RollB, Kg(1_000m));
        DeclareGood(order, RollA, 1_000m);

        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollA, 1m));
        order.Return(RollB, Kg(1_000m));

        Assert.Equal((0m, 0m), (order.BalanceOf(RollA).Unexplained, order.BalanceOf(RollB).Unexplained));
    }

    [Fact]
    public void A_paint_order_bounds_the_return_on_the_order_total_and_still_on_the_rolls_issued_kg()
    {
        // A: 1000 issued, 1050 good (50 above its own input); B: 1000 issued, 800 good. Order total: 150 unconsumed.
        var order = Order(ManufacturingProcess.Paint, RollA, RollB);
        order.Issue(RollA, Kg(1_000m));
        order.Issue(RollB, Kg(1_000m));
        order.Declare(RollA, OutputClass.GoodProduct, Kg(1_050m));
        order.Declare(RollB, OutputClass.GoodProduct, Kg(800m));
        Assert.Equal(150m, order.TotalBalance().Unexplained);

        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollB, 150.001m));
        order.Return(RollB, Kg(150m));

        Assert.Equal((0m, 150m), (order.TotalBalance().Unexplained, order.BalanceOf(RollB).Returned.Value));
        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(order, RollA, 0.001m));
    }

    [Fact]
    public void A_stored_return_recorded_after_its_output_before_the_bound_still_restores()
    {
        // Stored before D4: 1000 issued, 900 good declared, then 500 returned (only the issued bound applied).
        var t0 = DateTimeOffset.UnixEpoch.AddDays(1);
        var issue = new InputIssue(Guid.NewGuid(), RollA, Kg(1_000m), t0);
        var output = new OutputDeclaration(Guid.NewGuid(), RollA, OutputClass.GoodProduct, Kg(900m), t0.AddHours(1),
            CostResult.NotCalculated);
        var stored = new InputReturn(Guid.NewGuid(), RollA, Kg(500m), t0.AddHours(2));

        var restored = ProductionOrder.Restore(Guid.NewGuid(), Guid.NewGuid(), Company, WorkCenter,
            ManufacturingProcess.Slitting, null, [new PlannedInput(RollA, Kg(10_000m))], [issue], [stored], [output], [],
            null, null, 4);

        Assert.Equal((4L, -400m), (restored.Version, restored.BalanceOf(RollA).Unexplained));
        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsUnconsumed, Refused(restored, RollA, 1m));
    }
}
