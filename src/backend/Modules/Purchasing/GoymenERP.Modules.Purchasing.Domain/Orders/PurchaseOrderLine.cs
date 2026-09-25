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
        Guid? parentLineId, Guid? sourceRequestLineId)
    {
        Id = id;
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
        Kind = kind;
        ParentLineId = parentLineId;
        SourceRequestLineId = sourceRequestLineId;
        TechnicalQuantity = Quantity.Zero(quantity.Unit);
        AcceptedQuantity = Quantity.Zero(quantity.Unit);
    }

    public Guid Id { get; }
    public Guid ProductId { get; }

    /// <summary>The Settings unit this line is in (identity only); a technical line keeps its general line's unit.</summary>
    public Guid UnitId { get; }

    public Quantity Quantity { get; }
    public PurchaseOrderLineKind Kind { get; }
    public Guid? ParentLineId { get; }
    public Guid? SourceRequestLineId { get; }

    /// <summary>General lines only: the part already carved into technical lines.</summary>
    public Quantity TechnicalQuantity { get; private set; }

    public Quantity AcceptedQuantity { get; private set; }

    /// <summary>What this line itself still commits to: ordered minus technical slices.</summary>
    public Quantity CommittedQuantity => Quantity.Subtract(TechnicalQuantity);

    /// <summary>Committed minus accepted: the future supply still expected on this line.</summary>
    public Quantity RemainingQuantity => CommittedQuantity.Subtract(AcceptedQuantity);

    internal void AddTechnicalSlice(Quantity slice)
    {
        var next = TechnicalQuantity.Add(slice);
        if (next.Add(AcceptedQuantity).IsGreaterThan(Quantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: teknik kırılımlar ve kabul edilen pay ilk taahhüdü aşıyor.");
        }

        TechnicalQuantity = next;
    }

    internal void AddAcceptedShare(Quantity accepted)
    {
        var next = AcceptedQuantity.Add(accepted);
        if (next.IsGreaterThan(CommittedQuantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.InternalInvariant,
                "İç tutarsızlık: kabul edilen pay taahhüdü aşıyor.");
        }

        AcceptedQuantity = next;
    }
}
