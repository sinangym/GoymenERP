using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task Rejection_adapter_without_owned_execution_support_fails_closed_before_command_reads()
    {
        var fixture = new Fixture();
        var service = RejectionFenceService(fixture, new LegacyRejectionAdapter());
        var result = await service.RejectPlanAsync(Guid.NewGuid(), new(1, "Ýnsan gerekçesi"), "reject",
            "manufacturing.plans.approve", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Unavailable, result.Status);
        Assert.Empty(fixture.Calls);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task Rejection_fresh_replay_keeps_the_immutable_answer_without_reopening_the_plan()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        SeedLegacyPlanReplay(fixture, ManufacturingCommandNames.RejectPlan, "reject",
            new { planId = id, expectedVersion = 1L, reason = "Ýnsan gerekçesi" }, ManufacturingApiStatus.Ok,
            LegacyPlanView(id, 2));
        var owner = new RejectionExecutionAdapter();
        var result = await RejectionFenceService(fixture, owner).RejectPlanAsync(id, new(1, "Ýnsan gerekçesi"),
            "reject", "manufacturing.plans.approve", CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Ok, true, "corr-legacy"),
            (result.Status, result.IsReplay, result.OriginalCorrelationId));
        Assert.Equal((id, 2L), (result.Resource!.PlanId, result.Resource.Version));
        Assert.Equal(0, owner.Mutations);
        Assert.Single(fixture.Store.Commands);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task Stored_rejection_replay_is_refused_when_authority_changes_after_the_owner_read(string fault)
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        SeedLegacyPlanReplay(fixture, ManufacturingCommandNames.RejectPlan, "reject",
            new { planId = id, expectedVersion = 1L, reason = "Ýnsan gerekçesi" }, ManufacturingApiStatus.Ok,
            LegacyPlanView(id, 2));
        var owner = new RejectionExecutionAdapter
        {
            AfterExecute = () =>
            {
                if (fault == "permission") fixture.Authority.Scope = GoymenERP.BuildingBlocks.Security.PermissionScope.Denied;
                if (fault == "actor") fixture.User.ActorId = OtherActor;
                if (fault == "company") fixture.CompanyContext.SwitchForTest(Guid.NewGuid());
            },
        };
        var result = await RejectionFenceService(fixture, owner).RejectPlanAsync(id, new(1, "Ýnsan gerekçesi"),
            "reject", "manufacturing.plans.approve", CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Denied, false), (result.Status, result.IsReplay));
        Assert.Equal(0, owner.Mutations);
        Assert.Single(fixture.Store.Commands);
    }

    [Fact]
    public async Task Rejection_save_race_rolls_back_before_one_replay_only_attempt_without_retrying_the_mutation()
    {
        var fixture = new Fixture();
        var owner = new RejectionExecutionAdapter { Race = true };
        var result = await RejectionFenceService(fixture, owner).RejectPlanAsync(Guid.NewGuid(), new(1, "Ýnsan gerekçesi"),
            "reject", "manufacturing.plans.approve", CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingStoreCodes.ConcurrentChange), (result.Status, result.Code));
        Assert.Equal(2, owner.Executions);
        Assert.Equal(1, owner.Mutations);
        Assert.True(owner.Aborted);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public async Task Rejection_race_reads_the_winning_immutable_receipt_only_after_the_owned_attempt_aborts()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        var owner = new RejectionExecutionAdapter
        {
            Race = true,
            AfterAbort = () => SeedLegacyPlanReplay(fixture, ManufacturingCommandNames.RejectPlan, "reject",
                new { planId = id, expectedVersion = 1L, reason = "Ýnsan gerekçesi" }, ManufacturingApiStatus.Ok,
                LegacyPlanView(id, 2)),
        };
        var result = await RejectionFenceService(fixture, owner).RejectPlanAsync(id, new(1, "Ýnsan gerekçesi"),
            "reject", "manufacturing.plans.approve", CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Ok, true), (result.Status, result.IsReplay));
        Assert.Equal("corr-legacy", result.OriginalCorrelationId);
        Assert.Equal(2, owner.Executions);
        Assert.Equal(1, owner.Mutations);
        Assert.Single(fixture.Store.Commands);
    }

    private static ManufacturingCommandService RejectionFenceService(Fixture fixture, IManufacturingPlanLifecycleStore owner) =>
        new(fixture.Store, fixture.Store, fixture.Store, new ManufacturingMassVerifier(fixture.Settings), fixture.Authority,
            fixture.CompanyContext, fixture.User, fixture.Correlation, new FixedTime(), manufacturingTolerances: fixture.Settings,
            planLifecycle: owner);

    private class LegacyRejectionAdapter : IManufacturingPlanLifecycleStore
    {
        public virtual Task<ProductionPlan> RejectPlanAsync(Guid id, long version, Guid actor, DateTimeOffset at,
            string reason, ManufacturingCommandMetadata metadata, CancellationToken token = default) =>
            throw new InvalidOperationException("No native mutation expected.");
        public Task<ProductionPlan> CancelPlanAsync(Guid id, long version, Guid actor, DateTimeOffset at,
            string reason, ManufacturingCommandMetadata metadata, CancellationToken token = default) =>
            throw new InvalidOperationException("Not used.");
    }

    // Service orchestration double only; the real transaction/audit rollback proof is in the SQL partial.
    private sealed class RejectionExecutionAdapter : LegacyRejectionAdapter, IManufacturingPlanLifecycleStore
    {
        public Action? AfterExecute { get; init; }
        public Action? AfterAbort { get; init; }
        public bool Race { get; init; }
        public int Executions { get; private set; }
        public int Mutations { get; private set; }
        public bool Aborted { get; private set; }
        public async Task<ManufacturingApiResult<PlanViewV1>> ExecutePlanRejectionAsync(
            Func<CancellationToken, Task<ManufacturingApiResult<PlanViewV1>>> execute,
            Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            Executions++;
            if (Executions > 1) Assert.True(Aborted);
            try
            {
                await fence(token);
                var result = await execute(token);
                AfterExecute?.Invoke();
                await fence(token);
                return result;
            }
            catch { Aborted = true; AfterAbort?.Invoke(); throw; }
        }
        public override Task<ProductionPlan> RejectPlanAsync(Guid id, long version, Guid actor, DateTimeOffset at,
            string reason, ManufacturingCommandMetadata metadata, CancellationToken token = default)
        {
            Mutations++;
            if (Race) throw new ManufacturingStoreException(ManufacturingStoreCodes.ConcurrentChange, "Synthetic save race.");
            return base.RejectPlanAsync(id, version, actor, at, reason, metadata, token);
        }
    }
}
