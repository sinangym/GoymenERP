using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// MFG-3, SİPARİŞ #292 P3-a: every mutation of a plan or an order names the version it read. A stale
/// version is refused whole, before any rule runs; a recorded change advances the version by one; a
/// refused rule leaves it where it was. Two callers that read the same order cannot both write.
/// </summary>
public sealed class VersionFenceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-00000000c001");
    private static readonly Guid MasterRoll = Guid.Parse("30000000-0000-0000-0000-00000000a001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-00000000b001");
    private static readonly Guid Closer = Guid.Parse("40000000-0000-0000-0000-00000000b002");

    private static Kilograms Kg(decimal value) => new(value);

    private static ProductionPlan DraftPlan() =>
        new(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting, [new PlannedInput(MasterRoll, Kg(10_000m))]);

    private static ProductionOrder Order()
    {
        var plan = DraftPlan();
        plan.Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        return ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
    }

    [Fact]
    public void A_plan_approval_names_the_version_it_read()
    {
        var plan = DraftPlan();
        Assert.Equal(1, plan.Version);

        var stale = Assert.Throws<ManufacturingVersionConflictException>(
            () => plan.Approve(plan.Version + 1, Planner, DateTimeOffset.UnixEpoch));

        Assert.Equal((ManufacturingRuleCodes.VersionConflict, 2L, 1L), (stale.Code, stale.ExpectedVersion, stale.CurrentVersion));
        Assert.Equal(PlanStatus.Draft, plan.Status);
        Assert.Null(plan.ApprovedByUserId);

        plan.Approve(1, Planner, DateTimeOffset.UnixEpoch);
        Assert.Equal((PlanStatus.Approved, 2L), (plan.Status, plan.Version));

        // A second approval with the CURRENT version reaches the rule; with the old one it is a conflict.
        Assert.Equal(ManufacturingRuleCodes.PlanNotDraft, Assert.Throws<ManufacturingRuleException>(
            () => plan.Approve(2, Planner, DateTimeOffset.UnixEpoch)).Code);
        Assert.Throws<ManufacturingVersionConflictException>(() => plan.Approve(1, Planner, DateTimeOffset.UnixEpoch));
        Assert.Equal(2, plan.Version);
    }

    [Fact]
    public void Every_recorded_fact_advances_the_order_version_by_one()
    {
        var order = Order();
        Assert.Equal(1, order.Version);

        order.IssueInput(1, Guid.NewGuid(), MasterRoll, Kg(10_000m), OrderSteps.Next());
        order.ReturnUnusedInput(2, Guid.NewGuid(), MasterRoll, Kg(300m), OrderSteps.Next());
        order.DeclareOutput(3, Guid.NewGuid(), MasterRoll, OutputClass.GoodProduct, Kg(9_700m), OrderSteps.Next());
        order.RecordWorkInProgress(4, Guid.NewGuid(), MasterRoll, Kg(0m), OrderSteps.Next());
        Assert.Equal(5, order.Version);

        order.Close(5, Closer, DateTimeOffset.UnixEpoch);
        Assert.Equal((OrderStatus.Closed, 6L), (order.Status, order.Version));
    }

    [Fact]
    public void A_stale_version_is_refused_before_any_rule_and_changes_nothing()
    {
        var order = Order();
        order.Issue(MasterRoll, Kg(10_000m));
        var read = order.Version - 1;

        void Stale(Action write)
        {
            var error = Assert.Throws<ManufacturingVersionConflictException>(write);
            Assert.Equal((ManufacturingRuleCodes.VersionConflict, read, 2L), (error.Code, error.ExpectedVersion, error.CurrentVersion));
        }

        Stale(() => order.IssueInput(read, Guid.NewGuid(), MasterRoll, Kg(1m), OrderSteps.Next()));
        Stale(() => order.ReturnUnusedInput(read, Guid.NewGuid(), MasterRoll, Kg(1m), OrderSteps.Next()));
        Stale(() => order.DeclareOutput(read, Guid.NewGuid(), MasterRoll, OutputClass.Scrap, Kg(1m), OrderSteps.Next()));
        Stale(() => order.RecordWorkInProgress(read, Guid.NewGuid(), MasterRoll, Kg(1m), OrderSteps.Next()));
        Stale(() => order.Close(read, Closer, DateTimeOffset.UnixEpoch));
        // The fence comes first: even a write that would break a rule answers the conflict.
        Stale(() => order.IssueInput(read, Guid.NewGuid(), Guid.NewGuid(), Kg(1m), OrderSteps.Next()));

        Assert.Equal(2, order.Version);
        Assert.Single(order.Issues);
        Assert.Empty(order.Returns);
        Assert.Empty(order.Outputs);
        Assert.Empty(order.WipMeasurements);
        Assert.Equal(OrderStatus.Open, order.Status);
    }

    [Fact]
    public void A_refused_rule_leaves_the_version_where_it_was()
    {
        var order = Order();
        order.Issue(MasterRoll, Kg(10_000m));

        Assert.Equal(ManufacturingRuleCodes.ReturnExceedsIssued, Assert.Throws<ManufacturingRuleException>(
            () => order.ReturnUnusedInput(order.Version, Guid.NewGuid(), MasterRoll, Kg(10_001m), OrderSteps.Next())).Code);
        Assert.Equal(ManufacturingRuleCodes.UnexplainedDifference, Assert.Throws<ManufacturingRuleException>(
            () => order.Close(order.Version, Closer, DateTimeOffset.UnixEpoch)).Code);

        Assert.Equal(2, order.Version);
        Assert.Equal(OrderStatus.Open, order.Status);
    }

    [Fact]
    public void Two_callers_that_read_the_same_order_cannot_both_write()
    {
        var order = Order();
        var bothRead = order.Version;

        order.IssueInput(bothRead, Guid.NewGuid(), MasterRoll, Kg(6_000m), OrderSteps.Next());
        Assert.Throws<ManufacturingVersionConflictException>(
            () => order.IssueInput(bothRead, Guid.NewGuid(), MasterRoll, Kg(4_000m), OrderSteps.Next()));

        Assert.Equal(6_000m, order.BalanceOf(MasterRoll).Issued.Value);
    }
}
