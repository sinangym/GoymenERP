using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// MFG-1 (T15, Dilme/Sarım): plan v2 §7.3 and the §13 production example. The numbers below are
/// the example's own (10.000 kg rulo → 9.700 bant + 200 hurda + 100 kayıp); no tolerance, trim or
/// formula value is assumed anywhere.
/// </summary>
public sealed class SlittingOrderTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000c001");
    private static readonly Guid MasterRoll = Guid.Parse("30000000-0000-0000-0000-00000000a001");
    private static readonly Guid OtherRoll = Guid.Parse("30000000-0000-0000-0000-00000000a002");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000b001");

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionPlan DraftPlan(params PlannedInput[] inputs) =>
        new(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            inputs.Length == 0 ? new[] { new PlannedInput(MasterRoll, Kg(10_000m)) } : inputs);

    private static ProductionOrder ApprovedOrder(params PlannedInput[] inputs)
    {
        var plan = DraftPlan(inputs);
        plan.Approve(Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    [Fact]
    public void Approving_a_plan_consumes_nothing()
    {
        var plan = DraftPlan();
        plan.Approve(Planner, DateTimeOffset.UnixEpoch);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);

        Assert.Equal(PlanStatus.Approved, plan.Status);
        var balance = order.BalanceOf(MasterRoll);
        Assert.Equal(0m, balance.Issued.Value);
        Assert.Empty(order.Outputs);
        // Nothing issued means nothing to account for — not a hidden loss.
        Assert.Equal(0m, balance.Unexplained);
    }

    [Fact]
    public void An_order_is_created_only_from_an_approved_plan()
    {
        var error = Assert.Throws<ManufacturingRuleException>(
            () => ProductionOrder.FromApprovedPlan(Guid.NewGuid(), DraftPlan()));
        Assert.Equal("plan_not_approved", error.Code);
    }

    [Fact]
    public void A_plan_is_approved_once()
    {
        var plan = DraftPlan();
        plan.Approve(Planner, DateTimeOffset.UnixEpoch);

        var error = Assert.Throws<ManufacturingRuleException>(() => plan.Approve(Planner, DateTimeOffset.UnixEpoch));
        Assert.Equal("plan_not_draft", error.Code);
    }

    [Fact]
    public void Section13_slitting_example_balances_with_every_class_kept_apart()
    {
        var order = ApprovedOrder();
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(4_850m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(4_850m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(200m));
        order.Declare(MasterRoll, OutputClass.ProcessLoss, Kg(100m));

        var balance = order.BalanceOf(MasterRoll);

        Assert.Equal(9_700m, balance.OutputOf(OutputClass.GoodProduct).Value);
        Assert.Equal(200m, balance.OutputOf(OutputClass.Scrap).Value);
        Assert.Equal(100m, balance.OutputOf(OutputClass.ProcessLoss).Value);
        Assert.Equal(0m, balance.OutputOf(OutputClass.SecondQuality).Value);
        Assert.Equal(0m, balance.OutputOf(OutputClass.UsableRemnant).Value);
        Assert.Equal(10_000m, balance.DeclaredOutputs.Value);
        Assert.Equal(0m, balance.Unexplained);
        Assert.True(balance.IsFullyAccounted);
    }

    [Fact]
    public void An_unexplained_difference_is_shown_and_never_becomes_process_loss()
    {
        var order = ApprovedOrder();
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(200m));

        var balance = order.BalanceOf(MasterRoll);

        Assert.Equal(100m, balance.Unexplained);
        Assert.False(balance.IsFullyAccounted);
        Assert.Equal(0m, balance.OutputOf(OutputClass.ProcessLoss).Value);
        Assert.DoesNotContain(order.Outputs, output => output.Class == OutputClass.ProcessLoss);
    }

    [Fact]
    public void Declaring_more_than_was_issued_is_shown_as_a_negative_difference_not_hidden()
    {
        var order = ApprovedOrder();
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(10_100m));

        Assert.Equal(-100m, order.BalanceOf(MasterRoll).Unexplained);
    }

    [Fact]
    public void Every_output_is_bound_to_an_issued_parent_roll_of_this_order()
    {
        var order = ApprovedOrder();

        var notIssued = Assert.Throws<ManufacturingRuleException>(
            () => order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(1m)));
        Assert.Equal("roll_not_issued", notIssued.Code);

        order.Issue(MasterRoll, Kg(10_000m));
        var foreign = Assert.Throws<ManufacturingRuleException>(
            () => order.Declare(OtherRoll, OutputClass.GoodProduct, Kg(1m)));
        Assert.Equal("roll_not_in_order", foreign.Code);

        var declared = order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(1m));
        Assert.Equal(MasterRoll, declared.ParentRollId);
    }

    [Fact]
    public void Input_can_be_issued_only_from_a_roll_the_plan_names()
    {
        var order = ApprovedOrder();
        var error = Assert.Throws<ManufacturingRuleException>(() => order.Issue(OtherRoll, Kg(10m)));
        Assert.Equal("roll_not_in_order", error.Code);
    }

    [Fact]
    public void Partial_production_accounts_the_rest_as_work_in_progress()
    {
        var order = ApprovedOrder();
        order.Issue(MasterRoll, Kg(10_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(3_000m));
        order.MeasureWip(MasterRoll, Kg(7_000m));

        var first = order.BalanceOf(MasterRoll);
        Assert.Equal(7_000m, first.WorkInProgress.Value);
        Assert.True(first.IsFullyAccounted);

        // The next partial step: more bands, the in-process mass measured again.
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(6_700m));
        order.Declare(MasterRoll, OutputClass.Scrap, Kg(200m));
        order.Declare(MasterRoll, OutputClass.ProcessLoss, Kg(100m));
        order.MeasureWip(MasterRoll, Kilograms.Zero);

        var last = order.BalanceOf(MasterRoll);
        Assert.Equal(9_700m, last.OutputOf(OutputClass.GoodProduct).Value);
        Assert.Equal(0m, last.WorkInProgress.Value);
        Assert.True(last.IsFullyAccounted);
    }

    [Fact]
    public void Unused_input_is_returned_and_can_never_exceed_what_was_issued()
    {
        var order = ApprovedOrder();
        order.Issue(MasterRoll, Kg(10_000m));
        order.Return(MasterRoll, Kg(2_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(8_000m));

        var balance = order.BalanceOf(MasterRoll);
        Assert.Equal(8_000m, balance.NetIssued.Value);
        Assert.True(balance.IsFullyAccounted);

        var error = Assert.Throws<ManufacturingRuleException>(
            () => order.Return(MasterRoll, Kg(8_000.01m)));
        Assert.Equal("return_exceeds_issued", error.Code);
    }

    [Fact]
    public void Each_parent_roll_has_its_own_account()
    {
        var order = ApprovedOrder(new PlannedInput(MasterRoll, Kg(10_000m)), new PlannedInput(OtherRoll, Kg(5_000m)));
        order.Issue(MasterRoll, Kg(10_000m));
        order.Issue(OtherRoll, Kg(5_000m));
        order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(10_000m));
        order.Declare(OtherRoll, OutputClass.GoodProduct, Kg(4_000m));

        Assert.True(order.BalanceOf(MasterRoll).IsFullyAccounted);
        // Another roll's surplus never covers this roll's gap.
        Assert.Equal(1_000m, order.BalanceOf(OtherRoll).Unexplained);
    }

    [Fact]
    public void An_output_is_declared_once()
    {
        var order = ApprovedOrder();
        order.Issue(MasterRoll, Kg(10_000m));
        var outputId = Guid.NewGuid();
        order.DeclareOutput(outputId, MasterRoll, OutputClass.GoodProduct, Kg(100m), OrderSteps.Next());

        var error = Assert.Throws<ManufacturingRuleException>(
            () => order.DeclareOutput(outputId, MasterRoll, OutputClass.Scrap, Kg(100m), OrderSteps.Next()));
        Assert.Equal("output_already_declared", error.Code);
    }

    [Fact]
    public void Every_output_carries_an_uncalculated_cost_never_zero()
    {
        var order = ApprovedOrder();
        order.Issue(MasterRoll, Kg(10_000m));
        var declared = order.Declare(MasterRoll, OutputClass.GoodProduct, Kg(9_700m));

        Assert.Equal(CostStatus.NotCalculated, declared.Cost.Status);
        Assert.Null(declared.Cost.Amount);
        Assert.Null(CostResult.WaitingPolicy.Amount);
        // No known cost can be constructed in this slice: its money shape belongs to T25/Finance.
        Assert.All(order.Outputs, output => Assert.Equal(CostStatus.NotCalculated, output.Cost.Status));
    }

    [Fact]
    public void Masses_are_kept_exactly_to_three_decimals_and_never_negative()
    {
        // Ö2: kilograms carry at most three decimals; a fourth is refused, never rounded or cut.
        Assert.Equal(0.001m, new Kilograms(0.001m).Value);
        Assert.Equal(12.5m, new Kilograms(12.5000m).Value);
        Assert.Throws<ArgumentException>(() => new Kilograms(0.0005m));
        Assert.Throws<ArgumentException>(() => new Kilograms(9_700.0001m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Kilograms(-0.001m));
    }

    [Fact]
    public void The_order_keeps_its_own_snapshot_of_the_planned_inputs()
    {
        var plan = DraftPlan(new PlannedInput(MasterRoll, Kg(10_000m)));
        plan.Approve(Planner, DateTimeOffset.UnixEpoch);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);

        Assert.NotSame(plan.Inputs, order.PlannedInputs);
        Assert.Equal(plan.Inputs, order.PlannedInputs);
        Assert.Equal(plan.Id, order.PlanId);
        Assert.Equal(Company, order.CompanyId);
    }

    [Fact]
    public void A_plan_names_each_parent_roll_once_and_at_least_one()
    {
        Assert.Throws<ArgumentException>(() => new ProductionPlan(
            Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting, []));
        Assert.Throws<ArgumentException>(() => DraftPlan(
            new PlannedInput(MasterRoll, Kg(1m)), new PlannedInput(MasterRoll, Kg(2m))));
    }
}
