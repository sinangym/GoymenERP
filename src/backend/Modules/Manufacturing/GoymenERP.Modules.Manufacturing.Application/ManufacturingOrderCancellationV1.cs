using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

// F-009 D5 (URT-D33/D34/D35/D39; docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md §5 D5): an order that left no net effect
// is cancelled (never deleted); the cancel releases its plan and the plan's allocation generation in the same transaction;
// the released plan opens a new order only after a minimal Approved→Approved re-approval on unchanged Settings.

/// <summary>
/// F-009 D5 (URT-D33/D34): the order cancel in one save — the order row's status and cancel columns, the plan row's
/// release (plan version), both audit events and the command record — or nothing. The domain decides on the order and plan
/// rebuilt from storage.
/// </summary>
public interface IManufacturingOrderCancellationStore
{
    /// <summary>Cancels the order and releases its plan; the reason is already trimmed and validated.</summary>
    Task<ProductionOrder> CancelOrderAsync(Guid orderId, long expectedVersion, Guid cancelledByUserId,
        DateTimeOffset cancelledAt, string reason, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// F-009 D5 (URT-D35/D39): the minimal re-approval of a released plan in one save — the re-approval row, the plan's
/// version, the audit event and the command record — or nothing. The first approval and its snapshot are not changed.
/// </summary>
public interface IManufacturingPlanReapprovalStore
{
    /// <summary>
    /// Re-approves the plan; <paramref name="currentSettings"/> is the Settings profile in force now and must be the one
    /// the first approval captured (URT-D39); <paramref name="labelUsable"/> is Inventory's current label answer.
    /// </summary>
    Task<ProductionPlan> ReapprovePlanAsync(Guid planId, long expectedVersion, Guid reapprovedByUserId,
        DateTimeOffset reapprovedAt, PlanParameterSnapshotV1? currentSettings, IReadOnlyDictionary<Guid, bool>? labelUsable,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary><c>POST /orders/{orderId}/cancellation</c>: the order version the caller read and the cancel reason.</summary>
public sealed record CancelOrderRequestV1(long ExpectedVersion, string? Reason);

/// <summary><c>POST /plans/{planId}/reapproval</c>: the plan version the caller read (the version after the release).</summary>
public sealed record ReapprovePlanRequestV1(long ExpectedVersion, IReadOnlyList<Guid>? SelectedLabelIds = null);

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// <c>POST /orders/{orderId}/cancellation</c> (URT-D33/D34) under <c>manufacturing.orders.cancel</c> (registered centrally,
    /// granted to no role — a human gate): an open order with no net effect (no net output, every issued kilogram returned,
    /// no WIP reading above zero) is cancelled with a reason and kept. Its plan forgets it (plan version +1) and the plan's
    /// remaining allocation is released through the allocation port in the same transaction; with no port or no store
    /// bound the cancel is refused (503) and nothing is written. A closed or cancelled order and an order with a net effect
    /// are 422; a stale version is 409. ADR-011: the same key replays the first answer.
    /// </summary>
    public Task<ManufacturingApiResult<OrderViewV1>> CancelOrderAsync(Guid orderId, CancelOrderRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<OrderViewV1>("expectedVersion"));
        }

        if (DecisionReason(request.Reason) is not { } reason)
        {
            return Task.FromResult(Invalid<OrderViewV1>("reason"));
        }

        return RunAsync<OrderViewV1>(permission, idempotencyKey, ManufacturingCommandNames.CancelOrder,
            new { orderId, expectedVersion = request.ExpectedVersion, reason }, ManufacturingApiStatus.Ok,
            async (metadata, actor, _) =>
            {
                var current = await orders.FindOrderAsync(orderId, cancellationToken);
                if (current is null)
                {
                    return NotFoundResult<OrderViewV1>();
                }

                // No fail-open: without the store or the allocation port the plan's allocation could not be released with
                // the cancel, so nothing is written.
                if (orderCancellations is null || planAllocations is null)
                {
                    return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                        ManufacturingPlanAllocationCodesV1.Incomplete);
                }

                var cancelledAt = timeProvider.GetUtcNow();
                Task<ProductionOrder> Cancel(CancellationToken token) => orderCancellations.CancelOrderAsync(orderId,
                    request.ExpectedVersion, actor, cancelledAt, reason, metadata, token);
                var cancelled = await planAllocations.WriteAndReleaseAsync(current.PlanId,
                    ManufacturingPlanAllocationReleaseReasonV1.OrderCancelled, Cancel, metadata, cancellationToken);
                return new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Ok, OrderView(cancelled), false, null);
            }, stored => OrderView((ProductionOrder)stored), cancellationToken);
    }

    /// <summary>
    /// <c>POST /plans/{planId}/reapproval</c> (URT-D35/D39) under <c>manufacturing.plans.approve</c>: the approved plan
    /// released by an order cancel is re-approved on the Settings profile its first approval captured, and its inputs are
    /// allocated again (a new allocation generation at the new plan version) at the label versions Inventory holds now.
    /// A changed profile is 422 <c>manufacturing_plan_reapproval_settings_changed</c> (the plan is revised through a new
    /// plan); the preparer is 422 <c>manufacturing_plan_self_approval</c>; no release waiting is 422; a stale version 409;
    /// no allocation port or store 503. The first approval is not changed.
    /// </summary>
    public Task<ManufacturingApiResult<PlanViewV1>> ReapprovePlanAsync(Guid planId, ReapprovePlanRequestV1? request,
        string idempotencyKey, string permission, CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PlanViewV1>("expectedVersion"));
        }

        object canonical = new { planId, expectedVersion = request.ExpectedVersion };
        if (request.SelectedLabelIds is { } selection)
            canonical = new { planId, expectedVersion = request.ExpectedVersion, selectedLabelIds = selection.Order().ToArray() };
        return RunAsync<PlanViewV1>(permission, idempotencyKey, ManufacturingCommandNames.ReapprovePlan,
            canonical, ManufacturingApiStatus.Ok,
            async (metadata, actor, companyId) =>
            {
                if (planReapprovals is null || planAllocations is null)
                {
                    return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                        ManufacturingPlanAllocationCodesV1.Incomplete);
                }

                PlanParameterSnapshotV1? snapshot = null;
                IReadOnlyDictionary<Guid, bool>? labelUsable = null;
                IReadOnlyDictionary<Guid, long> labelVersions = new Dictionary<Guid, long>();
                var current = await plans.FindPlanAsync(planId, cancellationToken);
                if (current is not null && current.Version == request.ExpectedVersion)
                {
                    // The stored state answers first: an unreleased, cancelled or self-prepared plan never queries Settings
                    // or Inventory; the store's domain decision refuses it again at the save. A missing or stale plan goes
                    // straight to the store, whose refusal (not found, version fence) is the key's answer.
                    current.RequireReapprovable();
                    if (request.SelectedLabelIds is not { Count: > 0 } selected
                        || selected.Contains(Guid.Empty) || selected.Distinct().Count() != selected.Count)
                        return Invalid<PlanViewV1>("selectedLabelIds");
                    // URT-D35: explicit user selection; this minimal D5 path admits only the unchanged planned labels.
                    // D5b owns a changed label's recalculation and complete revision history.
                    if (!selected.Order().SequenceEqual(current.Inputs.Select(input => input.ParentRollId).Order()))
                        return new(ManufacturingApiStatus.Unprocessable, null, false,
                            ManufacturingRuleCodes.PlanReapprovalLabelMismatch);

                    if (current.IsPreparedBy(actor))
                    {
                        return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unprocessable, null, false,
                            ManufacturingRuleCodes.PlanSelfApproval);
                    }

                    var parameters = await ReadParameterProfileAsync(current.Process, current.WorkCenterId,
                        approval: true, cancellationToken);
                    if (parameters.Refusal is not null)
                    {
                        return Refusal<PlanViewV1>(parameters.Refusal);
                    }

                    if (await FenceCapturedAsync<PlanViewV1>(permission, companyId, actor, cancellationToken) is { } fenced)
                    {
                        return fenced;
                    }

                    snapshot = parameters.Snapshot;
                    // URT-D39: a changed profile refuses before Inventory is read; the domain refuses it again at the save.
                    if (!PlanParameterSnapshotV1.SameSettingsAs(current.ParameterSnapshot, snapshot))
                    {
                        return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Unprocessable, null, false,
                            ManufacturingRuleCodes.PlanReapprovalSettingsChanged);
                    }

                    var versions = await ReadCurrentLabelVersionsAsync(current, permission, companyId, actor,
                        cancellationToken);
                    if (versions.Refusal is not null)
                    {
                        return Refusal<PlanViewV1>(versions.Refusal);
                    }

                    labelVersions = versions.Versions!;
                    if (current.Spec is not null)
                    {
                        var labels = await ReadLabelUsabilityAsync(current, permission, companyId, actor,
                            cancellationToken, labelVersions);
                        if (labels.Refusal is not null)
                        {
                            return Refusal<PlanViewV1>(labels.Refusal);
                        }

                        labelUsable = labels.Usable;
                    }
                }

                var reapprovedAt = timeProvider.GetUtcNow();
                if (current is not null && current.Version == request.ExpectedVersion
                    && await VerifyPaintSelectionAsync(current.ProcessSpec, current.Process, permission, companyId, actor, cancellationToken, approval: true) is { } paintRefusal)
                    return Refusal<PlanViewV1>(paintRefusal);

                Task<ProductionPlan> Reapprove(CancellationToken token) => planReapprovals.ReapprovePlanAsync(planId,
                    request.ExpectedVersion, actor, reapprovedAt, snapshot, labelUsable, metadata, token);
                var reapproved = await planAllocations.ApproveAndAllocateAsync(planId, labelVersions, Reapprove,
                    metadata, token => RequirePlanAllocationDecisionAuthorityAsync(permission, companyId, actor, token), cancellationToken);
                return new ManufacturingApiResult<PlanViewV1>(ManufacturingApiStatus.Ok, PlanView(reapproved), false, null);
            }, stored => PlanView((ProductionPlan)stored), cancellationToken);
    }
}
