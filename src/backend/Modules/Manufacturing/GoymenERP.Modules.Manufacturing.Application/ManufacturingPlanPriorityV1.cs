using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

public sealed record PlanPriorityViewV1(Guid CompanyId, Guid PlanId, long PlanVersion, int PriorityScore,
    long PriorityRevision, string? RecordedBy, DateTimeOffset? RecordedAtUtc, string? Reason);
public sealed record ChangePlanPriorityRequestV1(long ExpectedVersion, long ExpectedPriorityRevision,
    int PriorityScore, string? Reason);

public interface IManufacturingPlanPriorityStore
{
    Task<PlanPriorityViewV1?> FindPlanPriorityAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<PlanPriorityViewV1> ChangePlanPriorityAsync(Guid planId, long expectedVersion, long expectedPriorityRevision,
        int score, string reason, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

public sealed record PlanWorkCenterQueueLineV1(Guid PlanId, DateOnly? DueDate, int PriorityScore,
    long PriorityRevision, string PlannedKg, string ProcessedKg, string RemainingKg, bool IsOverPlanned, bool IsQueued);
public sealed record PlanWorkCenterQueueViewV1(int ContractVersion, Guid CompanyId, Guid WorkCenterId,
    Guid TargetPlanId, long TargetPlanVersion, IReadOnlyList<PlanWorkCenterQueueLineV1> Items,
    long QueueRevision = 0, bool HasManualOrder = false);

public sealed partial class ManufacturingCommandService
{
    public async Task<ManufacturingApiResult<PlanPriorityViewV1>> GetPlanPriorityAsync(Guid planId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId) return Denied<PlanPriorityViewV1>();
        var companyId = companyContext.CompanyId;
        var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.ManufacturingPlansRead, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !HasCapturedContext(companyId, actorId)) return Denied<PlanPriorityViewV1>();
        ManufacturingApiResult<PlanPriorityViewV1> answer;
        if (planPriorities is null) answer = new(ManufacturingApiStatus.Unavailable, null, false, null);
        else
        {
            try
            {
                var value = await planPriorities.FindPlanPriorityAsync(planId, cancellationToken);
                answer = value is null ? NotFoundResult<PlanPriorityViewV1>()
                    : value.CompanyId != companyId || value.PlanId != planId
                        ? new(ManufacturingApiStatus.Unavailable, null, false, null)
                        : new(ManufacturingApiStatus.Ok, value, false, null);
            }
            catch (ManufacturingStoreException) { answer = new(ManufacturingApiStatus.Unavailable, null, false, null); }
        }
        scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.ManufacturingPlansRead, companyId, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide && HasCapturedContext(companyId, actorId) ? answer : Denied<PlanPriorityViewV1>();
    }

    public Task<ManufacturingApiResult<PlanPriorityViewV1>> ChangePlanPriorityAsync(Guid planId,
        ChangePlanPriorityRequestV1? request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (request is null || planId == Guid.Empty || request.ExpectedVersion <= 0 || request.ExpectedPriorityRevision < 0)
            return Task.FromResult(Invalid<PlanPriorityViewV1>("expectedVersion"));
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > ProductionPlanPriorityV1.ReasonMaximumLength)
            return Task.FromResult(Invalid<PlanPriorityViewV1>("reason"));
        return RunAsync<PlanPriorityViewV1>(ErpPermissions.ManufacturingPlansApprove, idempotencyKey,
            "manufacturing.plan.priority.v1", new { planId, request.ExpectedVersion, request.ExpectedPriorityRevision,
                request.PriorityScore, Reason = request.Reason.Trim() }, ManufacturingApiStatus.Ok,
            async (metadata, _, _) => planPriorities is null
                ? new(ManufacturingApiStatus.Unavailable, null, false, null)
                : new(ManufacturingApiStatus.Ok, await planPriorities.ChangePlanPriorityAsync(planId,
                    request.ExpectedVersion, request.ExpectedPriorityRevision, request.PriorityScore, request.Reason.Trim(),
                    metadata, cancellationToken), false, null), stored => (PlanPriorityViewV1)stored, cancellationToken);
    }

    public async Task<ManufacturingApiResult<PlanWorkCenterQueueViewV1>> GetPlanWorkCenterQueueAsync(Guid planId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId) return Denied<PlanWorkCenterQueueViewV1>();
        var companyId = companyContext.CompanyId;
        var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.ManufacturingPlansRead, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !HasCapturedContext(companyId, actorId)) return Denied<PlanWorkCenterQueueViewV1>();
        ManufacturingApiResult<PlanWorkCenterQueueViewV1> answer;
        try
        {
            var plan = await plans.FindPlanAsync(planId, cancellationToken);
            if (plan is null || plan.CompanyId != companyId) answer = NotFoundResult<PlanWorkCenterQueueViewV1>();
            else if (workCenterQueues is null) answer = new(ManufacturingApiStatus.Unavailable, null, false, null);
            else
            {
                var version = plan.Version;
                var queue = await workCenterQueues.ReadWorkCenterQueueAsync(plan.WorkCenterId, plan.Id, cancellationToken);
                var current = await plans.FindPlanAsync(planId, cancellationToken);
                if (current is null || current.Version != version || current.CompanyId != companyId || queue.Target.PlanId != plan.Id)
                    answer = new(ManufacturingApiStatus.Unavailable, null, false, null);
                else
                {
                    var items = ProductionWorkCenterQueueOrderV1.Realized(queue.Queued, queue.Target, queue.TargetIsQueued, queue.StoredOrder)
                        .Select(entry => new PlanWorkCenterQueueLineV1(entry.PlanId, entry.DueDate, entry.PriorityScore,
                            entry.PriorityRevision, Text(entry.PlannedInputKg.Value), Text(entry.ProcessedKg.Value),
                            Text(ProductionQueueRemainingKgCalculatorV1.Remaining(entry.PlannedInputKg, entry.ProcessedKg).Value),
                            entry.ProcessedKg.Value > entry.PlannedInputKg.Value,
                            entry.PlanId != plan.Id || queue.TargetIsQueued)).ToArray();
                    answer = new(ManufacturingApiStatus.Ok, new(1, companyId, plan.WorkCenterId, plan.Id, version, items,
                        queue.QueueRevision, queue.StoredOrder is not null), false, null);
                }
            }
        }
        catch (ManufacturingStoreException) { answer = new(ManufacturingApiStatus.Unavailable, null, false, null); }
        catch (ManufacturingRuleException) { answer = new(ManufacturingApiStatus.Unavailable, null, false, null); }
        scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.ManufacturingPlansRead, companyId, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide && HasCapturedContext(companyId, actorId) ? answer : Denied<PlanWorkCenterQueueViewV1>();
    }
}
