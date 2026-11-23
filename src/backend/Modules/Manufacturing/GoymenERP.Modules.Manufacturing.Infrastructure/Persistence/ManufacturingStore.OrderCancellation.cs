using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// F-009 D5 (URT-D33/D34/D35/D39): the order cancel with its plan release, and the plan's minimal re-approval. Each is
/// one save: the rows, the audit events and the command record commit together or not at all. Nothing is deleted.
/// </summary>
public sealed partial class ManufacturingStore : IManufacturingOrderCancellationStore, IManufacturingPlanReapprovalStore
{
    /// <summary>
    /// F-009 D5 (URT-D33/D34): the order and its plan are rebuilt from storage; the domain cancels the order (no net
    /// effect left) and the plan forgets it (plan version +1, release recorded). Both rows move in one save; the order row
    /// keeps the plan version its release produced, so the plan's rebuild replays the release at that version.
    /// </summary>
    public async Task<ProductionOrder> CancelOrderAsync(Guid orderId, long expectedVersion, Guid cancelledByUserId,
        DateTimeOffset cancelledAt, string reason, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var orderRow = await dbContext.Orders.SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.OrderNotFound, "Bu şirketin böyle bir emri yok.");
        var planRow = await dbContext.Plans.SingleOrDefaultAsync(candidate => candidate.Id == orderRow.PlanId,
                cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.PlanNotFound, "Bu şirketin böyle bir planı yok.");
        var order = await RestoreOrderAsync(orderRow, cancellationToken);
        var plan = await RestorePlanAsync(planRow, cancellationToken);
        var orderVersionBefore = order.Version;
        var planVersionBefore = plan.Version;
        var planBefore = PlanAudit(plan);

        // The domain decides: the order's version fence, open, a reason, no net effect; then the plan's release.
        order.Cancel(expectedVersion, cancelledByUserId, cancelledAt, reason);
        plan.ReleaseCancelledOrder(order.Id);

        var now = timeProvider.GetUtcNow();
        orderRow.Status = (int)order.Status;
        orderRow.CancelledByUserId = order.CancelledByUserId;
        orderRow.CancelledAtUtc = order.CancelledAtUtc;
        orderRow.CancellationReason = order.CancellationReason;
        orderRow.PlanVersionAfterCancel = plan.Version;
        orderRow.Version = order.Version;
        orderRow.UpdatedAtUtc = now;
        planRow.Version = plan.Version;
        planRow.UpdatedAtUtc = now;

        Audit(metadata, companyId, OrderEntityType, order.Id, "manufacturing.order.cancelled.v1", "Cancel",
            orderVersionBefore, order.Version, before: JsonSerializer.Serialize(new { status = "Open" }, Json),
            after: JsonSerializer.Serialize(new
            {
                status = "Cancelled",
                planId = plan.Id,
                planVersionAfterCancel = plan.Version,
                reason = order.CancellationReason,
            }, Json),
            order.CancellationReason!);
        Audit(metadata, companyId, PlanEntityType, plan.Id, "manufacturing.plan.order-released.v1", "ReleaseOrder",
            planVersionBefore, plan.Version, planBefore, PlanAudit(plan), order.CancellationReason!);
        Complete(metadata, companyId, order);
        await SaveAsync(cancellationToken);
        return order;
    }

    /// <summary>
    /// F-009 D5 (URT-D35/D39): the plan is rebuilt from storage; the domain re-approves it on the current Settings profile
    /// (which must be the one the first approval captured) and the slitting gate. The first approval's columns and snapshot
    /// rows are not touched; the re-approval row, the plan version, the audit event and the command record commit together.
    /// </summary>
    public async Task<ProductionPlan> ReapprovePlanAsync(Guid planId, long expectedVersion, Guid reapprovedByUserId,
        DateTimeOffset reapprovedAt, PlanParameterSnapshotV1? currentSettings, IReadOnlyDictionary<Guid, bool>? labelUsable,
        ManufacturingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var row = await dbContext.Plans.SingleOrDefaultAsync(candidate => candidate.Id == planId, cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.PlanNotFound, "Bu şirketin böyle bir planı yok.");
        var plan = await RestorePlanAsync(row, cancellationToken);
        var before = PlanAudit(plan);
        var versionBefore = plan.Version;
        RequireSnapshotFits(currentSettings);

        // The domain decides: the fence, the re-approvable state, the preparer, unchanged Settings, the slitting gate. The
        // gate reads the first approval's capture (its plan-entered values included).
        plan.Reapprove(expectedVersion, reapprovedByUserId, reapprovedAt, currentSettings,
            SlittingPlanParameters.FromCaptured(plan.Inputs, plan.ParameterSnapshot ?? currentSettings, labelUsable));
        var reapproval = plan.Reapprovals[^1];

        dbContext.PlanReapprovals.Add(new PlanReapprovalRow
        {
            CompanyId = companyId,
            PlanId = plan.Id,
            ReapprovalNo = reapproval.Number,
            PlanVersion = reapproval.PlanVersion,
            ReapprovedByUserId = reapproval.ReapprovedByUserId,
            ReapprovedAtUtc = reapproval.ReapprovedAtUtc,
            CorrelationId = metadata.CorrelationId.Trim(),
        });
        row.Version = plan.Version;
        row.UpdatedAtUtc = timeProvider.GetUtcNow();
        Audit(metadata, companyId, PlanEntityType, plan.Id, "manufacturing.plan.reapproved.v1", "Reapprove",
            versionBefore, plan.Version, before, PlanAudit(plan), "Üretim planı yeniden onayı");
        Complete(metadata, companyId, plan);
        await SaveAsync(cancellationToken);
        return plan;
    }
}
