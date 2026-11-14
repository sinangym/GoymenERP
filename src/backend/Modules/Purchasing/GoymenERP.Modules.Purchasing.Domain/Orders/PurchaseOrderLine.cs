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
    /// the line (a later price is a revision, ALS-D9/D18, A2b); a technical line never carries one (its quantity is priced on
    /// its general line).
    /// </summary>
    public PurchaseOrderLinePrice? Price { get; }

    /// <summary>General lines only: the part already carved into technical lines.</summary>
    public Quantity TechnicalQuantity { get; private set; }

    public Quantity AcceptedQuantity { get; private set; }

    /// <summary>A1-3 (No.161 §2): the not-yet-accepted remainder cancelled with the order (zero until then).</summary>
    public Quantity CancelledQuantity { get; private set; }

    /// <summary>What this line itself still commits to: ordered minus technical slices.</summary>
    public Quantity CommittedQuantity => Quantity.Subtract(TechnicalQuantity);

    /// <summary>
    /// Committed minus accepted minus cancelled: the future supply still expected on this line (master INV-006:
    /// Onaylı − İptal − KabulNet; the return effect arrives with A5).
    /// </summary>
    public Quantity RemainingQuantity => CommittedQuantity.Subtract(AcceptedQuantity).Subtract(CancelledQuantity);

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
        if (next.Add(AcceptedQuantity).Add(CancelledQuantity).IsGreaterThan(Quantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: teknik kırılımlar ve kabul edilen pay ilk taahhüdü aşıyor.");
        }

        TechnicalQuantity = next;
    }

    internal void AddAcceptedShare(Quantity accepted)
    {
        var next = AcceptedQuantity.Add(accepted);
        if (next.Add(CancelledQuantity).IsGreaterThan(CommittedQuantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: kabul edilen pay taahhüdü aşıyor.");
        }

        AcceptedQuantity = next;
    }

    /// <summary>A1-3: the line's whole remainder becomes its cancelled share (zero when nothing remains); only the aggregate calls it.</summary>
    internal void CancelRemainder()
    {
        if (!CancelledQuantity.IsZero)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: satırın kalanı zaten iptal edilmiş.");
        }

        CancelledQuantity = RemainingQuantity;
    }
}
