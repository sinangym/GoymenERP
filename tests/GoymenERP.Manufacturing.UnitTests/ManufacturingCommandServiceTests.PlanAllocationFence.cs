using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task Native_approval_never_falls_back_to_an_older_unfenced_allocation_adapter()
    {
        var fixture = new Fixture(); var plan = AvailabilityPlan(fixture); plan.WithDueDate();
        var owner = new LegacyOnlyAllocationAdapter();
        var answer = await AllocationFenceService(fixture, owner).ApprovePlanAsync(plan.Id, new(plan.Version),
            "legacy-approval", ErpPermissions.ManufacturingPlansApprove, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Unavailable, ManufacturingPlanAllocationCodesV1.Incomplete), (answer.Status, answer.Code));
        Assert.Equal(0, owner.OldCalls);
        Assert.Empty(fixture.Store.Commands); Assert.Empty(fixture.Store.Writes);
    }

    [Theory]
    [InlineData(false, "permission")]
    [InlineData(false, "actor")]
    [InlineData(false, "company")]
    [InlineData(true, "permission")]
    [InlineData(true, "actor")]
    [InlineData(true, "company")]
    public async Task Native_approval_and_explicit_label_reapproval_pass_the_captured_fresh_authority_to_the_owner(bool reapproval, string fault)
    {
        var fixture = new Fixture(); var plan = AvailabilityPlan(fixture); plan.WithDueDate();
        if (reapproval)
        {
            plan.Approve(plan.Version, OtherActor, At);
            var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
            order.Cancel(order.Version, Actor, At, "Synthetic cancelled order");
            plan.ReleaseCancelledOrder(order.Id);
        }
        var owner = new BoundaryAllocationAdapter(() =>
        {
            if (fault == "permission") fixture.Authority.Scope = PermissionScope.Denied;
            if (fault == "actor") fixture.User.ActorId = OtherActor;
            if (fault == "company") fixture.CompanyContext.SwitchForTest(OtherCompany);
        });
        var service = AllocationFenceService(fixture, owner);
        var answer = reapproval
            ? await service.ReapprovePlanAsync(plan.Id, new(plan.Version, [Roll]), "fresh-reapproval", ErpPermissions.ManufacturingPlansApprove, CancellationToken.None)
            : await service.ApprovePlanAsync(plan.Id, new(plan.Version), "fresh-approval", ErpPermissions.ManufacturingPlansApprove, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Denied, answer.Status);
        Assert.Equal(1, owner.Fences);
        Assert.Equal(0, owner.OldCalls);
        Assert.Empty(fixture.Store.Commands); Assert.Empty(fixture.Store.Writes);
    }

    private static ManufacturingCommandService AllocationFenceService(Fixture fixture, IManufacturingPlanAllocationPortV1 owner) =>
        new(fixture.Store, fixture.Store, fixture.Store, new ManufacturingMassVerifier(fixture.Settings), fixture.Authority,
            fixture.CompanyContext, fixture.User, fixture.Correlation, new FixedTime(), planAllocations: owner,
            planReapprovals: new ReapprovalMustNotSave(), labelDetails: new AllocationLabelDetails());

    private class LegacyOnlyAllocationAdapter : IManufacturingPlanAllocationPortV1
    {
        public int OldCalls { get; private set; }
        public Task<T> ApproveAndAllocateAsync<T>(Guid id, IReadOnlyDictionary<Guid, long> versions,
            Func<CancellationToken, Task<T>> approve, ManufacturingCommandMetadata metadata, CancellationToken token)
        { OldCalls++; return approve(token); }
        public Task<T> WriteAndReleaseAsync<T>(Guid id, ManufacturingPlanAllocationReleaseReasonV1 reason,
            Func<CancellationToken, Task<T>> write, ManufacturingCommandMetadata metadata, CancellationToken token) =>
            throw new InvalidOperationException("Not used.");
    }
    private sealed class BoundaryAllocationAdapter(Action changeAuthority) : LegacyOnlyAllocationAdapter, IManufacturingPlanAllocationPortV1
    {
        public int Fences { get; private set; }
        public async Task<T> ApproveAndAllocateAsync<T>(Guid id, IReadOnlyDictionary<Guid, long> versions,
            Func<CancellationToken, Task<T>> approve, ManufacturingCommandMetadata metadata,
            Func<CancellationToken, Task> freshDecisionFence, CancellationToken token)
        {
            changeAuthority(); Fences++;
            await freshDecisionFence(token);
            throw new InvalidOperationException("A revoked captured authority must refuse before the native write.");
        }
    }
}
