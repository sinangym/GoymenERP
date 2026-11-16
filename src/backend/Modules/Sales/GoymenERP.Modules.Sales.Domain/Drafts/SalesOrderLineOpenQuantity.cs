namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// KLN-D100 (SINAN-CEVAP 34.23; completes KLN-D28 (4) and KLN-D65): one order line's open quantity, measured on the
/// <b>delivered (shipped)</b> quantity, never on the invoiced one.
/// <list type="bullet">
/// <item><b>Open</b> = line quantity − net delivered, not below zero; <b>net delivered</b> = the owner's delivered total
/// (cancelled shipments already left out by the delivery owner) − the approved customer returns. A return is a correction
/// of the delivered quantity and re-opens what it takes back.</item>
/// <item><b>Excess</b> = net delivered above the line quantity (shown, not judged: the delivery tolerance stays SAT-D5's).</item>
/// <item><b>Invoiced</b> does not set the open quantity. When the invoiced quantity is known, <b>delivered not invoiced</b> =
/// net delivered − invoiced, not below zero, and is shown separately. Unknown invoiced is <c>null</c>, never zero.</item>
/// </list>
/// Exact in the line's own unit (Ö2): no conversion, no rounding; a quantity in another unit is refused
/// (<see cref="SalesRuleCodes.UnitMismatch"/>). An approved return above the delivered total is not a correction any rule
/// covers, so it is refused (<see cref="SalesRuleCodes.OpenQuantityReturnAboveDelivered"/>) instead of being shown as a
/// plausible open quantity.
/// </summary>
public sealed record SalesOrderLineOpenQuantity
{
    private const string ReturnAboveDeliveredMessage =
        "Onaylı müşteri iadesi teslim edilen miktardan büyük; açık miktar hesaplanmadı.";

    private SalesOrderLineOpenQuantity(Quantity ordered, Quantity delivered, Quantity returned, Quantity netDelivered,
        Quantity open, Quantity excess, Quantity? invoiced, Quantity? deliveredNotInvoiced)
    {
        Ordered = ordered;
        Delivered = delivered;
        Returned = returned;
        NetDelivered = netDelivered;
        Open = open;
        Excess = excess;
        Invoiced = invoiced;
        DeliveredNotInvoiced = deliveredNotInvoiced;
    }

    public Quantity Ordered { get; }
    public Quantity Delivered { get; }
    public Quantity Returned { get; }
    public Quantity NetDelivered { get; }
    public Quantity Open { get; }
    public Quantity Excess { get; }
    public Quantity? Invoiced { get; }
    public Quantity? DeliveredNotInvoiced { get; }

    /// <summary>
    /// Calculates the line's open quantity from the line quantity, the owner's delivered total, the approved customer returns
    /// and, when known, the invoiced quantity (used only for the separate "delivered not invoiced" figure).
    /// </summary>
    public static SalesOrderLineOpenQuantity Calculate(Quantity ordered, Quantity delivered, Quantity approvedReturned,
        Quantity? invoiced)
    {
        ordered.RequireSameUnit(delivered);
        ordered.RequireSameUnit(approvedReturned);
        if (invoiced is { } invoicedQuantity)
        {
            ordered.RequireSameUnit(invoicedQuantity);
        }

        if (approvedReturned.IsGreaterThan(delivered))
        {
            throw new SalesRuleException(SalesRuleCodes.OpenQuantityReturnAboveDelivered, ReturnAboveDeliveredMessage);
        }

        var netDelivered = Exact(delivered.Subtract(approvedReturned));
        var over = netDelivered.IsGreaterThan(ordered);
        var open = over ? Quantity.Zero(ordered.Unit) : Exact(ordered.Subtract(netDelivered));
        var excess = over ? Exact(netDelivered.Subtract(ordered)) : Quantity.Zero(ordered.Unit);
        Quantity? deliveredNotInvoiced = invoiced is { } billed
            ? (netDelivered.IsGreaterThan(billed) ? Exact(netDelivered.Subtract(billed)) : Quantity.Zero(ordered.Unit))
            : null;
        return new SalesOrderLineOpenQuantity(ordered, delivered, approvedReturned, netDelivered, open, excess, invoiced,
            deliveredNotInvoiced);
    }

    /// <summary>A difference keeps its operands' scale; re-taking it through the rule drops trailing zeros (Ö2).</summary>
    private static Quantity Exact(Quantity quantity) => Quantity.Of(quantity.Value, quantity.Unit);
}
