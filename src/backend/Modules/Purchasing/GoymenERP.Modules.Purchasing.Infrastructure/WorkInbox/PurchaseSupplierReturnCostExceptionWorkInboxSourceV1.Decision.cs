using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;

internal sealed partial class PurchaseSupplierReturnCostExceptionWorkInboxSourceV1
{
    // Exact company/return lookup plus one exact company/order lookup; no history scan or extra browse grant.
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (itemId == Guid.Empty || await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        var row = await dbContext.SupplierReturns.AsNoTracking().SingleOrDefaultAsync(r => r.CompanyId == context.CompanyId && r.ReturnId == itemId, token);
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null || row is null) return null;
        var order = await dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(r => r.CompanyId == context.CompanyId && r.Id == row.OrderId, token);
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        var disclosed = await fence.PricesDisclosedAsync(context, token);
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        var pending = row.Status == (int)PurchaseSupplierReturnStatus.PendingCostException;
        if (order is null || order.Version < 1 || row.Version < 1 || !Enum.IsDefined((PurchaseSupplierReturnStatus)row.Status)
            || row.UnitCostTryPerKg <= 0 || row.ReplacementExpected && row.ExpectedPurchaseOrderVersion is null
            || row.CostBasis != (int)PurchaseSupplierReturnCostBasis.ManualCostWithException
            || row.ReturnLineId == Guid.Empty || row.LineId == Guid.Empty || row.GoodsAcceptanceId == Guid.Empty || row.MovementId == Guid.Empty
            || row.SourceBlock is not ("Free" or "Blocked") || string.IsNullOrWhiteSpace(row.Reason)
            || row.Reason.Length > PurchaseSupplierReturn.MaximumReasonLength || row.OccurredAtUtc == default || row.OccurredAtUtc.Offset != TimeSpan.Zero
            || row.RequestedAtUtc == default || row.RequestedAtUtc.Offset != TimeSpan.Zero
            || (row.ExpectedPurchaseOrderVersion is null) != (row.UndoQuantity is null)
            || row.ExpectedPurchaseOrderVersion is < 1 || row.UndoQuantity is <= 0
            || (pending ? row.Version != 1 || row.DecidedBy is not null || row.DecidedAtUtc is not null || row.DecisionReason is not null
                : row.Version != 2 || string.IsNullOrWhiteSpace(row.DecidedBy) || row.DecidedBy == row.RequestedBy
                  || row.DecidedAtUtc is null || row.DecidedAtUtc == default(DateTimeOffset) || row.DecidedAtUtc.Value.Offset != TimeSpan.Zero
                  || (row.Status == (int)PurchaseSupplierReturnStatus.Rejected ? string.IsNullOrWhiteSpace(row.DecisionReason) : row.DecisionReason is not null))
            || !TryItem(context, row, new Dictionary<Guid, string?> { [order.Id] = order.DocumentNumber }, disclosed, out var item))
            throw new InvalidOperationException("Supplier return decision proof is unavailable.");
        var proof = new WorkSupplierReturnCostExceptionProofV1(row.CompanyId, row.ReturnId, row.ReturnLineId, row.OrderId, row.LineId,
            row.GoodsAcceptanceId, row.MovementId, row.SourceBlock, WorkInboxRulesV1.Decimal(row.QuantityKg),
            ((PurchaseSupplierReturnCostBasis)row.CostBasis).ToString(), row.ReplacementExpected, row.Reason, row.OccurredAtUtc,
            row.RequestedBy, row.RequestedAtUtc, row.ExpectedPurchaseOrderVersion?.ToString(CultureInfo.InvariantCulture),
            row.ExpectedPurchaseOrderVersion is null ? null : order.Version.ToString(CultureInfo.InvariantCulture),
            row.UndoQuantity is { } undo ? WorkInboxRulesV1.Decimal(undo) : null);
        return new(context.CompanyId, item, row.Version.ToString(CultureInfo.InvariantCulture), pending, SupplierReturnCostExceptionProof: proof);
    }
}
