using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task A_reapproval_requires_the_users_explicit_unchanged_label_selection_before_any_save()
    {
        var fixture = new Fixture();
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.Slitting,
            [new PlannedInput(Roll, new Kilograms(1000m))]);
        plan.WithDueDate().Approve(plan.Version, Guid.NewGuid(), At);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.Cancel(order.Version, Actor, At, "SENTETIK cancellation");
        plan.ReleaseCancelledOrder(order.Id);
        fixture.Store.Plans[plan.Id] = plan;
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(), planAllocations: fixture.PlanAllocations,
            planReapprovals: new ReapprovalMustNotSave());
        var missing = await service.ReapprovePlanAsync(plan.Id, new(plan.Version), "missing-selection",
            "manufacturing.plans.approve", CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Invalid, "selectedLabelIds"), (missing.Status, missing.Field));
        var changed = await service.ReapprovePlanAsync(plan.Id, new(plan.Version, [Guid.NewGuid()]), "changed-selection",
            "manufacturing.plans.approve", CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.PlanReapprovalLabelMismatch),
            (changed.Status, changed.Code));
        Assert.True(plan.RequiresReapproval);
        Assert.Empty(plan.Reapprovals);
        Assert.Empty(fixture.Store.Commands);
    }

    private sealed class ReapprovalMustNotSave : IManufacturingPlanReapprovalStore
    {
        public Task<ProductionPlan> ReapprovePlanAsync(Guid planId, long expectedVersion, Guid actor,
            DateTimeOffset at, PlanParameterSnapshotV1? settings, IReadOnlyDictionary<Guid, bool>? labels,
            ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Selection refusal must precede persistence.");
    }
}
