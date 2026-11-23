using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

public sealed record MoveManufacturingQueuePlanRequestV1(long ExpectedVersion, long ExpectedQueueRevision,
    Guid MovingPlanId, Guid? BeforePlanId, string? Reason);

public interface IManufacturingWorkCenterQueueOrderingStore
{
    Task<PlanWorkCenterQueueViewV1> MoveQueuePlanAsync(Guid targetPlanId, long expectedVersion, long expectedQueueRevision,
        Guid movingPlanId, Guid? beforePlanId, string reason, ManufacturingCommandMetadata metadata,
        Func<CancellationToken, Task<ManufacturingApiResult<object>?>> finalFence, CancellationToken cancellationToken = default);
}

public sealed class ManufacturingQueueVersionException(long expected, long current) : Exception("Kuyruk sürümü değişti.")
{
    public long Expected { get; } = expected;
    public long Current { get; } = current;
}

public sealed class ManufacturingQueueFenceException(ManufacturingApiResult<object> refusal) : Exception("Kuyruk yazma yetkisi doğrulanamadı.")
{
    public ManufacturingApiResult<object> Refusal { get; } = refusal;
}

public static class ManufacturingQueueViewsV1
{
    public static PlanWorkCenterQueueViewV1 Create(Guid companyId, Guid workCenterId, Guid targetPlanId,
        long targetVersion, ManufacturingWorkCenterQueue queue) => new(1, companyId, workCenterId, targetPlanId, targetVersion,
        ProductionWorkCenterQueueOrderV1.Realized(queue.Queued, queue.Target, queue.TargetIsQueued, queue.StoredOrder)
            .Select(entry => new PlanWorkCenterQueueLineV1(entry.PlanId, entry.DueDate, entry.PriorityScore,
                entry.PriorityRevision, Kg(entry.PlannedInputKg.Value), Kg(entry.ProcessedKg.Value),
                Kg(ProductionQueueRemainingKgCalculatorV1.Remaining(entry.PlannedInputKg, entry.ProcessedKg).Value),
                entry.ProcessedKg.Value > entry.PlannedInputKg.Value, entry.PlanId != targetPlanId || queue.TargetIsQueued)).ToArray(),
        queue.QueueRevision, queue.StoredOrder is not null);
    private static string Kg(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed partial class ManufacturingCommandService
{
    public async Task<ManufacturingApiResult<PlanWorkCenterQueueViewV1>> MoveQueuePlanAsync(Guid planId,
        MoveManufacturingQueuePlanRequestV1? request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (request is null || planId == Guid.Empty || request.ExpectedVersion < 1 || request.ExpectedQueueRevision < 0
            || request.MovingPlanId == Guid.Empty || request.BeforePlanId == Guid.Empty)
            return Invalid<PlanWorkCenterQueueViewV1>("queue");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > ProductionWorkCenterQueueOrderV1.ReasonMaximumLength)
            return Invalid<PlanWorkCenterQueueViewV1>("reason");
        if (!HasContext() || ActorGuid() is not { } capturedActor) return Denied<PlanWorkCenterQueueViewV1>();
        var capturedCompany = companyContext.CompanyId;
        if (await FenceCapturedAsync<object>(ErpPermissions.ManufacturingPlansRead, capturedCompany, capturedActor, cancellationToken) is { } readRefusal)
            return Refusal<PlanWorkCenterQueueViewV1>(readRefusal);
        var result = await RunAsync<PlanWorkCenterQueueViewV1>(ErpPermissions.ManufacturingPlansReorder, idempotencyKey,
            "manufacturing.queue.move.v1", new { planId, request.ExpectedVersion, request.ExpectedQueueRevision,
                request.MovingPlanId, request.BeforePlanId, Reason = request.Reason.Trim() }, ManufacturingApiStatus.Ok,
            async (metadata, actorId, companyId) =>
            {
                if (queueOrdering is null) return new(ManufacturingApiStatus.Unavailable, null, false, null);
                try
                {
                    var view = await queueOrdering.MoveQueuePlanAsync(planId, request.ExpectedVersion, request.ExpectedQueueRevision,
                        request.MovingPlanId, request.BeforePlanId, request.Reason.Trim(), metadata,
                        async token => await FenceCapturedAsync<object>(ErpPermissions.ManufacturingPlansReorder, companyId, actorId, token)
                            ?? await FenceCapturedAsync<object>(ErpPermissions.ManufacturingPlansRead, companyId, actorId, token), cancellationToken);
                    return new(ManufacturingApiStatus.Ok, view, false, null);
                }
                catch (ManufacturingQueueVersionException conflict)
                {
                    return new(ManufacturingApiStatus.Conflict, null, false, ManufacturingRuleCodes.VersionConflict,
                        "expectedQueueRevision", conflict.Expected, conflict.Current);
                }
                catch (ManufacturingQueueFenceException fence) { return Refusal<PlanWorkCenterQueueViewV1>(fence.Refusal); }
            }, stored => (PlanWorkCenterQueueViewV1)stored, cancellationToken);
        return await FenceCapturedAsync<object>(ErpPermissions.ManufacturingPlansRead, capturedCompany, capturedActor, cancellationToken) is { } finalRead
            ? Refusal<PlanWorkCenterQueueViewV1>(finalRead) : result;
    }
}
