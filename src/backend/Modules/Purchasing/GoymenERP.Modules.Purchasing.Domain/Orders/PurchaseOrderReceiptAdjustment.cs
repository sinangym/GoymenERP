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
    {
        RequireVersion(expectedVersion);
        _ = checked(Version + 1);
        Identity.Require(adjustmentId, nameof(adjustmentId));
        Identity.Require(acceptanceId, nameof(acceptanceId));
        var recordedActor = RequireActor(actor, nameof(actor));
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaximumReasonLength)
            throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentReasonInvalid,
                "Kabul düzeltmesi geçerli ve sınır içinde bir gerekçe ister.");
        if (Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Cancelled))
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderNotApproved,
                "Kabul düzeltmesi yalnız onay geçmişi olan siparişe yazılır.");
        if (receiptAdjustments.Any(item => item.AdjustmentId == adjustmentId))
            throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentDuplicate,
                "Aynı kabul düzeltmesi ikinci kez kaydedilemez.");

        var original = OriginalAcceptance(acceptanceId);
        var line = FindLine(original.LineId);
        var replacement = newAcceptedQuantity ?? Quantity.Zero(original.Quantity.Unit);
        if (undoQuantity.Unit != original.Quantity.Unit || replacement.Unit != original.Quantity.Unit)
            throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentInvalid,
                "Kabul düzeltmesi özgün kabulün biriminde olmalıdır.");
        var available = original.Quantity.Subtract(AlreadyUndone(acceptanceId));
        if (undoQuantity.IsZero || undoQuantity.IsGreaterThan(available))
            throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentExceedsAcceptance,
                "Geri alınan pay özgün kabulün henüz geri alınmamış miktarını aşamaz.");
        if ((newAcceptanceId is null) != replacement.IsZero || newAcceptanceId == Guid.Empty
            || (isSupplierReturn && (!replacement.IsZero || newAcceptanceId is not null)))
            throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentInvalid,
                "Yeni kabul kimliği ve miktarı birlikte verilir; tedarikçiye iade yeni kabul oluşturmaz.");
        if (newAcceptanceId is { } replacementId && acceptances.Any(item => item.AcceptanceId == replacementId))
            throw new PurchasingRuleException(PurchasingRuleCodes.DuplicateAcceptanceId,
                "Düzeltmenin yeni kabul kimliği daha önce kullanılmış.");

        var definitiveReturn = isSupplierReturn && !replacementExpected;
        var nextAccepted = line.AcceptedQuantity.Subtract(undoQuantity).Add(replacement);
        var nextReturned = definitiveReturn ? line.DefinitiveReturnQuantity.Add(undoQuantity) : line.DefinitiveReturnQuantity;
        if (nextAccepted.Add(nextReturned).Add(line.CancelledQuantity).Add(line.ClosedQuantity).IsGreaterThan(line.CommittedQuantity))
            throw new PurchasingRuleException(PurchasingRuleCodes.AcceptanceExceedsRemaining,
                "Yeni kabulün net artışı kalan taahhüdü aşamaz.");

        var general = line.Kind == PurchaseOrderLineKind.General ? line : FindLine(line.ParentLineId!.Value);
        var group = new[] { general }.Concat(TechnicalSlicesOf(general.Id)).ToArray();
        var before = AllocatableGroupQuantity(general, group);
        var netPositive = !definitiveReturn && undoQuantity.IsGreaterThan(replacement);
        var revived = netPositive && (Status == PurchaseOrderStatus.Cancelled || general.IsClosed);

        // Every refusal above precedes mutation. Existing cancellation/closure shares do not decrease:
        // decreasing them as well as acceptance would reopen twice the quantity (ALIS F017 section3.8).
        line.ApplyReceiptAdjustment(undoQuantity, replacement, definitiveReturn);
        if (netPositive)
        {
            if (Status == PurchaseOrderStatus.Cancelled) Status = PurchaseOrderStatus.Approved;
            if (general.IsClosed)
                foreach (var member in group) member.ReviveReceiptClosure();
        }
        if (newAcceptanceId is { } newId)
            acceptances.Add(new(newId, line.Id, replacement, now.ToUniversalTime()));
        Touch(now);
        var after = AllocatableGroupQuantity(general, group);
        var opened = after.IsGreaterThan(before) ? after.Subtract(before) : Quantity.Zero(after.Unit);
        var record = new PurchaseReceiptAdjustment(adjustmentId, acceptanceId, line.Id, undoQuantity,
            isSupplierReturn, replacementExpected, newAcceptanceId, replacement, recordedActor, reason,
            now.ToUniversalTime(), Version, revived, opened);
        receiptAdjustments.Add(record);
        return record;
    }

    private PurchaseAcceptanceShare OriginalAcceptance(Guid acceptanceId) =>
        acceptances.SingleOrDefault(item => item.AcceptanceId == acceptanceId)
        ?? throw new PurchasingRuleException(PurchasingRuleCodes.ReceiptAdjustmentAcceptanceNotFound,
            "Düzeltilecek özgün kabul bulunamadı.");

    private Quantity AllocatableGroupQuantity(PurchaseOrderLine general, IReadOnlyList<PurchaseOrderLine> group) =>
        Status != PurchaseOrderStatus.Approved || general.IsClosed
            ? Quantity.Zero(general.Quantity.Unit)
            : group.Aggregate(Quantity.Zero(general.Quantity.Unit), (total, item) => total.Add(item.RemainingQuantity));
}
