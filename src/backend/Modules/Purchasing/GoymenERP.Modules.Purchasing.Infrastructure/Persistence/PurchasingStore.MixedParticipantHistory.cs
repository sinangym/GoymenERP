using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    // Read-only on the participant's existing context/transaction. No command, audit append, save or transaction adoption.
    internal async Task<PurchaseOrder> RestoreParticipantOrderAsync(PurchaseOrderRow row,
        IReadOnlyList<PurchaseOrderLineRow> lines, IReadOnlyList<PurchaseAcceptanceRow> acceptances,
        IReadOnlyList<PurchaseOrderLineDeliverySliceRow> slices, IReadOnlyList<PurchaseOrderLineClosureEventRow> closures,
        IReadOnlyList<PurchaseOrderExchangeRateChangeRow> rates, IReadOnlyList<PurchaseReceiptAdjustmentRow> adjustments,
        IReadOnlyList<PurchaseOrderCancellationHistoryRow> cancellations, IReadOnlyList<PurchaseReceiptAdjustmentBatchRow> batches,
        Action requireParticipantTransaction, CancellationToken cancellationToken)
    {
        Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); requireParticipantTransaction();
            if (RequireResolvedCompany() != row.CompanyId)
                throw new InvalidOperationException("Participant company differs from the stored order.");
            return Task.CompletedTask;
        }
        await Fence(cancellationToken);
        var membership = await ReadServiceMembershipsAsync([row.Id], cancellationToken, Fence);
        // Absence remains the unchanged legacy native restore; never infer membership from supplier/date/current children.
        if (membership.Members.Count == 0)
            return RestoreOrder(row, lines, acceptances, slices, closures, rates, adjustments, cancellations, batches);
        var original = row.Status != (int)PurchaseOrderStatus.Draft
            ? await ReadMixedRestoreReceiptAsync(row, Fence, cancellationToken) : null;
        var history = await ReadMixedSupplierHistoryAsync(row, true, Fence, cancellationToken);
        await Fence(cancellationToken);
        return RestoreOrder(row, lines, acceptances, slices, closures, rates, adjustments, cancellations, batches,
            membership.Members, membership.Prepared, original, history);
    }
}
