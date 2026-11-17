using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Theory]
    [InlineData("1000.125", true)]
    [InlineData("1000.126", false)]
    public async Task A_slitting_draft_uses_the_labels_total_physical_mass_without_deducting_blocked_or_reserved_stock(
        string requestedKg, bool accepted)
    {
        var fixture = new Fixture();
        fixture.ParentRolls.PhysicalKg = "1000.125";
        fixture.ParentRolls.UnblockedPhysicalKg = "100";
        fixture.ParentRolls.BlockedPhysicalKg = "900.125";
        var request = At044Plan(requestedKg);

        var result = await fixture.Service.SavePlanAsync(request, "physical-cap", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal(accepted ? ManufacturingApiStatus.Created : ManufacturingApiStatus.Unprocessable, result.Status);
        if (accepted)
        {
            Assert.Equal(requestedKg, Assert.Single(result.Resource!.Inputs).PlannedKg);
            Assert.Single(fixture.Store.Writes);
        }
        else
        {
            Assert.Equal((ManufacturingRuleCodes.PlanLabelNotUsable, "inputs[0].plannedKg"), (result.Code, result.Field));
            Assert.Empty(fixture.Store.Plans);
            Assert.Empty(fixture.Store.Writes);
            Assert.Empty(fixture.Store.Commands);
            Assert.Empty(fixture.Store.AuditEvents);
        }
    }

    [Theory]
    [InlineData(ManufacturingProcess.ColdRolling)]
    [InlineData(ManufacturingProcess.TubeProfile)]
    [InlineData(ManufacturingProcess.CutToLength)]
    [InlineData(ManufacturingProcess.Paint)]
    public async Task The_AT044_slitting_decision_does_not_add_a_physical_cap_to_other_processes(ManufacturingProcess process)
    {
        var fixture = new Fixture();
        fixture.WorkCenters.Kind = process.ToString();
        fixture.ParentRolls.PhysicalKg = "1";

        var result = await fixture.Service.SavePlanAsync(At044Plan("2"), "other-process", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Created, result.Status);
        Assert.Single(fixture.Store.Writes);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("1.0001")]
    [InlineData("1.000")]
    [InlineData("01")]
    [InlineData("unreadable")]
    public async Task An_unreadable_physical_total_is_unavailable_and_never_becomes_a_fake_bound(string physicalKg)
    {
        var fixture = new Fixture();
        fixture.ParentRolls.PhysicalKg = physicalKg;

        var result = await fixture.Service.SavePlanAsync(At044Plan("1"), "bad-physical", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, InventoryParentRollCodesV1.Unavailable), (result.Status, result.Code));
        Assert.Empty(fixture.Store.Writes);
        Assert.Empty(fixture.Store.Commands);
        Assert.DoesNotContain("product", fixture.Calls);
    }

    [Fact]
    public async Task A_saved_slitting_draft_replays_its_original_answer_without_rechecking_a_changed_physical_total()
    {
        var fixture = new Fixture();
        fixture.ParentRolls.PhysicalKg = "1000.125";
        var request = At044Plan("1000.125");
        var saved = await fixture.Service.SavePlanAsync(request, "physical-replay", "manufacturing.plans.write", CancellationToken.None);
        fixture.ParentRolls.Exception = new InventoryParentRollException(503, InventoryParentRollCodesV1.Unavailable);
        var reads = fixture.ParentRolls.Asked;

        var replay = await fixture.Service.SavePlanAsync(request, "physical-replay", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Created, saved.Status);
        Assert.Equal(ManufacturingApiStatus.Created, replay.Status);
        Assert.True(replay.IsReplay);
        Assert.Equal(reads, fixture.ParentRolls.Asked);
        Assert.Single(fixture.Store.Writes);
    }

    [Theory]
    [InlineData(false, "1000.125", true)]
    [InlineData(false, "1000.126", false)]
    [InlineData(true, "1000.125", true)]
    [InlineData(true, "1000.126", false)]
    public async Task Draft_and_cancelled_order_plan_revisions_share_the_physical_cap_without_a_free_quantity_read(
        bool released, string requestedKg, bool accepted)
    {
        var fixture = new Fixture();
        fixture.ParentRolls.PhysicalKg = "1000.125";
        fixture.ParentRolls.UnblockedPhysicalKg = "100";
        fixture.ParentRolls.BlockedPhysicalKg = "900.125";
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, new Kilograms(900m), long.Parse(LabelVersion))]);
        if (released)
        {
            plan.WithDueDate().Approve(plan.Version, Guid.NewGuid(), At);
            var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
            order.Cancel(order.Version, Actor, At, "SENTETIK cancellation");
            plan.ReleaseCancelledOrder(order.Id);
        }
        fixture.Store.Plans[plan.Id] = plan;
        var version = plan.Version;

        var result = await fixture.Service.RevisePlanAsync(plan.Id, new RevisePlanRequestV1(version, WorkCenter,
            [new PlannedInputRequestV1(Roll, requestedKg, LabelVersion)], null, "SENTETIK physical bound"),
            "physical-revision", "manufacturing.plans.write", CancellationToken.None);

        Assert.Equal(accepted ? ManufacturingApiStatus.Ok : ManufacturingApiStatus.Unprocessable, result.Status);
        if (accepted)
        {
            Assert.Equal(requestedKg, Assert.Single(result.Resource!.Inputs).PlannedKg);
            Assert.Equal(version + 1, plan.Version);
            Assert.Single(fixture.Store.Writes);
            Assert.Equal(released, plan.RequiresReapproval);
        }
        else
        {
            Assert.Equal((ManufacturingRuleCodes.PlanLabelNotUsable, "inputs[0].plannedKg"), (result.Code, result.Field));
            Assert.Equal(version, plan.Version);
            Assert.Empty(fixture.Store.Writes);
            Assert.Empty(fixture.Store.Commands);
        }
    }

    private static SavePlanRequestV1 At044Plan(string kg) => new(Guid.NewGuid(), WorkCenter,
        [new PlannedInputRequestV1(Roll, kg, LabelVersion)], null);
}
