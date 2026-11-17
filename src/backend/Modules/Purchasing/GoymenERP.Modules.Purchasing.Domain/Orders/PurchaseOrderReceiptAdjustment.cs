using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

/// <summary>Immutable owner history. Original acceptance shares and previous cancellations are never overwritten.</summary>
public sealed record PurchaseReceiptAdjustment(Guid AdjustmentId, Guid AcceptanceId, Guid LineId,
    Quantity UndoQuantity, bool IsSupplierReturn, bool ReplacementExpected, Guid? NewAcceptanceId,
    Quantity NewAcceptedQuantity, string Actor, string Reason, DateTimeOffset OccurredAtUtc,
    long OrderVersionAfter, bool Revived, Quantity NetOpenedQuantity);

public sealed record PurchaseOrderCancellationEvent(long OrderVersionAfter, PurchaseOrderCancellation Cancellation);

public sealed partial class PurchaseOrder
{
    private readonly List<PurchaseReceiptAdjustment> receiptAdjustments = [];
    private readonly List<PurchaseOrderCancellationEvent> cancellationHistory = [];

    public IReadOnlyList<PurchaseReceiptAdjustment> ReceiptAdjustments => receiptAdjustments.AsReadOnly();
    /// <summary>The header's Cancellation is the latest entry; this history retains the first and every subsequent cancellation.</summary>
    public IReadOnlyList<PurchaseOrderCancellationEvent> CancellationHistory => cancellationHistory.AsReadOnly();

    public Quantity AlreadyUndone(Guid acceptanceId)
    {
        var original = OriginalAcceptance(acceptanceId);
        return receiptAdjustments.Where(item => item.AcceptanceId == acceptanceId)
            .Aggregate(Quantity.Zero(original.Quantity.Unit), (total, item) => total.Add(item.UndoQuantity));
    }

    /// <summary>
    /// ALC-S6 / ALS-D86/D91/D92/D93: one net owner operation, including API-44's replacement receipt.
    /// A zero-net correction never temporarily opens a cancelled or closed line. The ordinary acceptance gate is unchanged.
    /// </summary>
    public PurchaseReceiptAdjustment AdjustReceipt(long expectedVersion, Guid adjustmentId, Guid acceptanceId,
        Quantity undoQuantity, bool isSupplierReturn, bool replacementExpected, string actor, string? reason,
        DateTimeOffset now, Guid? newAcceptanceId = null, Quantity? newAcceptedQuantity = null)
        => AdjustReceipts(expectedVersion,
            [new PurchaseReceiptAdjustmentInput(adjustmentId, acceptanceId, undoQuantity, isSupplierReturn,
                replacementExpected, newAcceptanceId, newAcceptedQuantity)], actor, reason, now).Adjustments[0];

    private PurchaseAcceptanceShare OriginalAcceptance(Guid acceptanceId) =>
        acceptances.SingleOrDefault(item => item.AcceptanceId == acceptanceId)
        ?? throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentAcceptanceNotFound,
            "Düzeltilecek özgün kabul bulunamadı.");

    private Quantity AllocatableGroupQuantity(PurchaseOrderLine general, IReadOnlyList<PurchaseOrderLine> group) =>
        Status != PurchaseOrderStatus.Approved || general.IsClosed
            ? Quantity.Zero(general.Quantity.Unit)
            : group.Aggregate(Quantity.Zero(general.Quantity.Unit), (total, item) => total.Add(item.RemainingQuantity));
}
