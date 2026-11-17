using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task Manual_queue_uses_its_separate_permission_and_replays_one_exact_completion_without_approving_content()
    {
        var fixture = new Fixture(); var queue = new QueueOrderingPort(fixture);
        var service = ManualQueueService(fixture, queue); var planId = Guid.NewGuid();
        var request = new MoveManufacturingQueuePlanRequestV1(3, 2, Guid.NewGuid(), null, "Synthetic manager order");
        var applied = await service.MoveQueuePlanAsync(planId, request, "queue-exact", CancellationToken.None);
        var replay = await service.MoveQueuePlanAsync(planId, request, "queue-exact", CancellationToken.None);
        var mismatch = await service.MoveQueuePlanAsync(planId, request with { Reason = "Different body" }, "queue-exact", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, applied.Status); Assert.Equal(3L, applied.Resource!.QueueRevision);
        Assert.Equal(applied.Resource.QueueRevision, replay.Resource!.QueueRevision); Assert.True(replay.IsReplay);
        Assert.Equal(ManufacturingApiStatus.Conflict, mismatch.Status); Assert.Equal(1, queue.Writes);
        Assert.Contains(ErpPermissions.ManufacturingPlansRead, fixture.Authority.Permissions);
        Assert.Contains(ErpPermissions.ManufacturingPlansReorder, fixture.Authority.Permissions);
        Assert.DoesNotContain(ErpPermissions.ManufacturingPlansApprove, fixture.Authority.Permissions);
    }

    [Fact]
    public async Task Losing_read_access_hides_a_stored_manual_queue_response()
    {
        var fixture = new Fixture(); var queue = new QueueOrderingPort(fixture); var service = ManualQueueService(fixture, queue);
        var planId = Guid.NewGuid(); var request = new MoveManufacturingQueuePlanRequestV1(3, 0, Guid.NewGuid(), null, "Synthetic order");
        Assert.Equal(ManufacturingApiStatus.Ok, (await service.MoveQueuePlanAsync(planId, request, "queue-read-revoked", CancellationToken.None)).Status);
        fixture.Authority.Scope = PermissionScope.Denied;
        var refused = await service.MoveQueuePlanAsync(planId, request, "queue-read-revoked", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Denied, refused.Status); Assert.Null(refused.Resource); Assert.Equal(1, queue.Writes);
    }

    [Fact]
    public async Task A_final_queue_permission_fence_refuses_before_snapshot_or_command_completion()
    {
        var fixture = new Fixture(); var queue = new QueueOrderingPort(fixture) { BeforeFinalFence = () => fixture.Authority.Scope = PermissionScope.Denied };
        var refused = await ManualQueueService(fixture, queue).MoveQueuePlanAsync(Guid.NewGuid(),
            new(3, 0, Guid.NewGuid(), null, "Synthetic order"), "queue-final-fence", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Denied, refused.Status); Assert.Equal(0, queue.Writes);
        Assert.Empty(fixture.Store.Commands); Assert.Empty(fixture.Store.AuditEvents);
    }

    private static ManufacturingCommandService ManualQueueService(Fixture fixture, QueueOrderingPort queue) =>
        new(fixture.Store, fixture.Store, fixture.Store, new ManufacturingMassVerifier(fixture.Settings), fixture.Authority,
            fixture.CompanyContext, fixture.User, fixture.Correlation, new FixedTime(), queueOrdering: queue);

    private sealed class QueueOrderingPort(Fixture fixture) : IManufacturingWorkCenterQueueOrderingStore
    {
        public int Writes { get; private set; }
        public Action? BeforeFinalFence { get; init; }
        public async Task<PlanWorkCenterQueueViewV1> MoveQueuePlanAsync(Guid targetPlanId, long expectedVersion,
            long expectedQueueRevision, Guid movingPlanId, Guid? beforePlanId, string reason, ManufacturingCommandMetadata metadata,
            Func<CancellationToken, Task<ManufacturingApiResult<object>?>> finalFence, CancellationToken cancellationToken = default)
        {
            BeforeFinalFence?.Invoke();
            if (await finalFence(cancellationToken) is { } refused) throw new ManufacturingQueueFenceException(refused);
            var response = new PlanWorkCenterQueueViewV1(1, Company, WorkCenter, targetPlanId, expectedVersion, [], expectedQueueRevision + 1, true);
            if (metadata.Completion is { } completion) await fixture.Store.RecordCommandAsync(completion(response), cancellationToken);
            Writes++; return response;
        }
    }
}
