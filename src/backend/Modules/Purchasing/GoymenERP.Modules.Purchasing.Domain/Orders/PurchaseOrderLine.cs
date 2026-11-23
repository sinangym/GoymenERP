using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

public enum PurchaseOrderLineKind
{
    /// <summary>The first commitment as ordered; may be broken down into technical lines.</summary>
    General = 1,

    /// <summary>A slice carved out of a general line; its quantity is already counted there.</summary>
    Technical = 2,
}

/// <summary>
/// One line of a purchase order. For a general line <see cref="Quantity"/> is the first
/// commitment and <see cref="TechnicalQuantity"/> the part already moved into technical lines,
/// so its own commitment is the difference. For a technical line the quantity is the slice.
/// Accepted and remaining are shares (§7.2), never physical stock.
/// <para>
/// Every share bound is decided by <see cref="PurchaseOrder"/>; the checks in this class are a
/// second, defensive guard and report <see cref="PurchasingRuleCodes.InternalInvariant"/> so
/// they are never mistaken for the aggregate's rule.
/// </para>
/// </summary>
public sealed class PurchaseOrderLine
{
    private bool closureReopenedByReceiptAdjustment;
    internal PurchaseOrderLine(Guid id, Guid productId, Guid unitId, Quantity quantity, PurchaseOrderLineKind kind,
        Guid? parentLineId, Guid? sourceRequestLineId, PurchaseOrderLinePrice? price = null)
    {
        Price = price;
        Id = id;
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
        Kind = kind;
        ParentLineId = parentLineId;
        SourceRequestLineId = sourceRequestLineId;
        TechnicalQuantity = Quantity.Zero(quantity.Unit);
        AcceptedQuantity = Quantity.Zero(quantity.Unit);
        CancelledQuantity = Quantity.Zero(quantity.Unit);
        ClosedQuantity = Quantity.Zero(quantity.Unit);
        DefinitiveReturnQuantity = Quantity.Zero(quantity.Unit);
    }

    public Guid Id { get; }
    public Guid ProductId { get; }

    /// <summary>The Settings unit this line is in (identity only); a technical line keeps its general line's unit.</summary>
    public Guid UnitId { get; }

    public Quantity Quantity { get; }
    public PurchaseOrderLineKind Kind { get; }
    public Guid? ParentLineId { get; }
    public Guid? SourceRequestLineId { get; }

    /// <summary>
    /// A2a-1 (ALS-D5, ALS-D18): the line's manual price in the order's currency, or null — "fiyat bekliyor". Written once with
    /// the line, then explicitly cleared/re-entered for a supplier revision (ALS-D115); a technical line never carries one (its quantity is priced on
    /// its general line).
    /// </summary>
    public PurchaseOrderLinePrice? Price { get; private set; }

    internal void ReplaceSupplierRevisionPrice(PurchaseOrderLinePrice? price) => Price = price;

    /// <summary>
    /// AK-3 (SAT-D437): the theoretical kg per piece recorded at the order's first approval; only a piece general line carries one,
    /// null before approval, on a kilogram line and on a technical slice (it reads its general line's).
    /// </summary>
    public PurchaseOrderLineTheoreticalKilograms? TheoreticalKilograms { get; private set; }

    /// <summary>True for a piece-unit general line, the only kind that needs a theoretical kg snapshot before approval.</summary>
    public bool NeedsTheoreticalKilograms => Kind == PurchaseOrderLineKind.General && Quantity.Unit == QuantityUnit.Piece;

    internal void RecordTheoreticalKilograms(PurchaseOrderLineTheoreticalKilograms snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!NeedsTheoreticalKilograms || TheoreticalKilograms is not null)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.TheoreticalKilogramsNotApplicable,
                "Teorik kg/adet yalnız henüz kaydı olmayan adetli genel satıra yazılır.");
        }

        TheoreticalKilograms = snapshot;
    }

    /// <summary>General lines only: the part already carved into technical lines.</summary>
    public Quantity TechnicalQuantity { get; private set; }

    public Quantity AcceptedQuantity { get; private set; }

    /// <summary>ALS-D91: an unmarked supplier return is no longer accepted and does not reopen future supply.</summary>
    public Quantity DefinitiveReturnQuantity { get; private set; }

    public bool ReceiptClosureRevived => closureReopenedByReceiptAdjustment;

    /// <summary>A1-3 (No.161 §2): the not-yet-accepted remainder cancelled with the order (zero until then).</summary>
    public Quantity CancelledQuantity { get; private set; }

    /// <summary>
    /// API-11 (ALIS_F017 v1 §2/§3.6, ALS-D10/D12/D87): the not-yet-accepted remainder closed with the line's short-delivery closure
    /// (eksik teslim kapanışı) — a separate axis from <see cref="CancelledQuantity"/>; an explicit API-11 reopening clears it.
    /// Receipt adjustment revival preserves this historic share and opens only the net undone acceptance.
    /// On a technical slice it is the slice's part of its general line's closure.
    /// </summary>
    public Quantity ClosedQuantity { get; private set; }

    /// <summary>API-11: the general line's current closure (who, when, why), or null while the line is open.</summary>
    public PurchaseOrderLineClosure? Closure { get; private set; }

    /// <summary>
    /// API-11: the general line's latest closure or reopening (who, when, why, which direction), or null when it has none; a technical
    /// slice has none. The store records each new one as the line's closure history and its outbox message.
    /// </summary>
    public PurchaseOrderLineClosureChange? LastClosureChange { get; private set; }

    /// <summary>API-11: true while the line (or, for a technical slice, its general line) is closed.</summary>
    public bool IsClosed => !closureReopenedByReceiptAdjustment && (Closure is not null || !ClosedQuantity.IsZero);

    /// <summary>What this line itself still commits to: ordered minus technical slices.</summary>
    public Quantity CommittedQuantity => Quantity.Subtract(TechnicalQuantity);

    /// <summary>
    /// Committed minus accepted minus cancelled minus closed minus definitive returned: the future supply still expected
    /// on this line (master INV-006, ALIS_F017 v1 section2, API-11/API-44/API-60).
    /// </summary>
    public Quantity RemainingQuantity => CommittedQuantity.Subtract(AcceptedQuantity).Subtract(CancelledQuantity)
        .Subtract(ClosedQuantity).Subtract(DefinitiveReturnQuantity);

    /// <summary>
    /// A3 API-30 (ALS-D33): the line's current delivery schedule (termin dilimleri), empty until one is recorded; its slices sum to
    /// <see cref="Quantity"/>. Only <see cref="PurchaseOrder.SetLineDeliverySchedule"/> replaces it, always as a whole set.
    /// </summary>
    public IReadOnlyList<PurchaseOrderDeliverySlice> DeliverySchedule { get; private set; } = [];

    internal void ReplaceDeliverySchedule(IReadOnlyList<PurchaseOrderDeliverySlice> slices)
    {
        if (Kind != PurchaseOrderLineKind.General || slices.Count == 0
            || slices.Aggregate(Quantity.Zero(Quantity.Unit), (sum, slice) => sum.Add(slice.Quantity)).Value != Quantity.Value)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: termin dilimleri genel satırın sipariş miktarına eşit değil.");
        }

        DeliverySchedule = slices.ToArray();
    }

    internal void AddTechnicalSlice(Quantity slice)
    {
        var next = TechnicalQuantity.Add(slice);
        if (next.Add(AcceptedQuantity).Add(CancelledQuantity).Add(ClosedQuantity).Add(DefinitiveReturnQuantity).IsGreaterThan(Quantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: teknik kırılımlar ve kabul edilen pay ilk taahhüdü aşıyor.");
        }

        TechnicalQuantity = next;
    }

    internal void AddAcceptedShare(Quantity accepted)
    {
        var next = AcceptedQuantity.Add(accepted);
        if (next.Add(CancelledQuantity).Add(ClosedQuantity).Add(DefinitiveReturnQuantity).IsGreaterThan(CommittedQuantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: kabul edilen pay taahhüdü aşıyor.");
        }

        AcceptedQuantity = next;
    }

    /// <summary>A1-3: the line's whole remainder becomes its cancelled share (zero when nothing remains); only the aggregate calls it.</summary>
    internal void CancelRemainder()
    {
        // ALS-D92: after revival only the newly opened remainder is cancelled again.
        CancelledQuantity = CancelledQuantity.Add(RemainingQuantity);
    }

    /// <summary>
    /// API-11: the line's whole remainder becomes its closed share (zero allowed on a technical slice whose remainder is already
    /// gone); a general line also records <paramref name="closure"/>, a technical slice passes null. Only the aggregate calls it.
    /// </summary>
    internal void CloseRemainder(PurchaseOrderLineClosure? closure)
    {
        if (IsClosed || (closure is null) != (Kind == PurchaseOrderLineKind.Technical))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: satır zaten kapalı veya kapanış kaydı satır türüyle uyuşmuyor.");
        }

        ClosedQuantity = ClosedQuantity.Add(RemainingQuantity);
        Closure = closure;
        closureReopenedByReceiptAdjustment = false;
        if (closure is not null)
        {
            LastClosureChange = new PurchaseOrderLineClosureChange(true, closure.ClosedBy, closure.ClosedAtUtc, closure.Reason);
        }
    }

    /// <summary>
    /// API-11 reopening (ALS-D10): the closed share returns to the remainder and the closure record is cleared; a general line records
    /// <paramref name="reopening"/>, a technical slice passes null. Released future allocations do not come back (ALIS_F017 v1 §3.6,
    /// author reading, closed direction). Only the aggregate calls it.
    /// </summary>
    internal void Reopen(PurchaseOrderLineClosureChange? reopening)
    {
        var general = Kind == PurchaseOrderLineKind.General;
        if ((general && (!IsClosed || reopening is null || reopening.Closed)) || (!general && reopening is not null))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: kapalı olmayan satır yeniden açılamaz veya yeniden açma kaydı satır türüyle uyuşmuyor.");
        }

        ClosedQuantity = Quantity.Zero(Quantity.Unit);
        Closure = null;
        closureReopenedByReceiptAdjustment = false;
        if (reopening is not null)
        {
            LastClosureChange = reopening;
        }
    }

    internal void ApplyReceiptAdjustment(Quantity undone, Quantity newlyAccepted, bool definitiveReturn)
    {
        var accepted = AcceptedQuantity.Subtract(undone).Add(newlyAccepted);
        var returned = definitiveReturn ? DefinitiveReturnQuantity.Add(undone) : DefinitiveReturnQuantity;
        ApplyReceiptAdjustmentTotals(accepted, returned);
    }

    internal void ApplyReceiptAdjustmentTotals(Quantity accepted, Quantity returned)
    {
        if (accepted.Add(CancelledQuantity).Add(ClosedQuantity).Add(returned).IsGreaterThan(CommittedQuantity))
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: kabul düzeltmesinin payları taahhüdü aşıyor.");
        AcceptedQuantity = accepted;
        DefinitiveReturnQuantity = returned;
    }

    internal void ReviveReceiptClosure() => closureReopenedByReceiptAdjustment = true;
}
