using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task A_fresh_allocation_refusal_carries_the_owner_snapshot_without_recording_an_attempt_and_revocation_or_foreign_scope_hides_it(bool revoked, bool foreign)
    {
        var fixture = new Fixture(); var plan = AvailabilityPlan(fixture); plan.WithDueDate();
        var snapshot = new ManufacturingPlanAllocationShortfallV1(foreign ? OtherCompany : Company, plan.Id, Roll, Product, "8", "600", "350", "250", 1);
        var allocation = new RefusedAllocationPort(() => {
            if (revoked) fixture.Authority.Scope = PermissionScope.Denied;
            return new(ManufacturingApiStatus.Conflict, InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock,
                planAllocationShortfall: snapshot);
        });
        var service = new ManufacturingCommandService(fixture.Store, fixture.Store, fixture.Store,
            new ManufacturingMassVerifier(fixture.Settings), fixture.Authority, fixture.CompanyContext, fixture.User,
            fixture.Correlation, new FixedTime(), planAllocations: allocation, labelDetails: new AllocationLabelDetails());
        var answer = await service.ApprovePlanAsync(plan.Id, new(plan.Version), "synthetic-shortfall", ErpPermissions.ManufacturingPlansApprove, CancellationToken.None);
        Assert.Equal(revoked ? ManufacturingApiStatus.Denied : ManufacturingApiStatus.Conflict, answer.Status);
        Assert.Equal(revoked || foreign ? null : snapshot, answer.PlanAllocationShortfall);
        Assert.Empty(fixture.Store.Commands); Assert.Empty(fixture.Store.Writes); Assert.Equal(PlanStatus.Draft, plan.Status);
        Assert.DoesNotContain("planAllocationShortfall", JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Allocation_quantity_detail_is_optional_and_belongs_only_to_its_exact_conflict()
    {
        var snapshot = new ManufacturingPlanAllocationShortfallV1(Company, Guid.NewGuid(), Roll, Product, "8", "600", "350", "250", 1);
        Assert.Null(new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Conflict,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock).PlanAllocationShortfall);
        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Conflict,
            InventoryManufacturingRealizationCodesV1.LabelVersionConflict, planAllocationShortfall: snapshot));
        Assert.Throws<ArgumentException>(() => new ManufacturingPlanAllocationShortfallV1(Company, Guid.NewGuid(), Roll, Product,
            "8", "600.0001", "350", "250", 1));
    }

    private sealed class RefusedAllocationPort(Func<ManufacturingInventoryRealizationPortException> refusal) : IManufacturingPlanAllocationPortV1
    {
        public Task<T> ApproveAndAllocateAsync<T>(Guid planId, IReadOnlyDictionary<Guid, long> labelVersions,
            Func<CancellationToken, Task<T>> approve, ManufacturingCommandMetadata metadata,
            Func<CancellationToken, Task> freshDecisionFence, CancellationToken cancellationToken) =>
            Task.FromException<T>(refusal());
        public Task<T> WriteAndReleaseAsync<T>(Guid planId, ManufacturingPlanAllocationReleaseReasonV1 reason,
            Func<CancellationToken, Task<T>> write, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
    private sealed class AllocationLabelDetails : IInventoryLabelDetailsV1
    {
        public Task<InventoryLabelDetailV1> GetAsync(Guid labelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InventoryLabelDetailV1("8", new InventoryLabelViewV1(labelId, Company, "SYNTHETIC-LABEL", Product,
                Guid.NewGuid(), null, null, null, "Complete", [], [], [], new(Guid.NewGuid(), null, null)), null!, null!, null!));
    }
}
