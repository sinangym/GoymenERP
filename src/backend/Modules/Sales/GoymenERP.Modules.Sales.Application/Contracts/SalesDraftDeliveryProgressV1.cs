namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// AT-024: one order line's delivered and remaining quantity, exact decimal text in the line's order unit (Ö2, never
/// rounded). <c>ordered</c> is the line's quantity; <c>shipped</c> and <c>delivered</c> are the delivery owner's recorded
/// totals (Logistics, through <see cref="ISalesOrderDeliveryReadPortV1"/>); <c>remaining</c> = ordered − delivered, never
/// below zero; <c>excess</c> = delivered − ordered when more was delivered than ordered, else zero. These are facts only:
/// whether a shortfall closes the line or an excess is acceptable is the over/under-delivery tolerance policy (OPEN-004,
/// not decided), so no tolerance, warning or completion state is derived here.
/// </summary>
public sealed record SalesDraftLineDeliveryProgressV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Ordered,
    string Shipped,
    string Delivered,
    string Remaining,
    string Excess);

/// <summary>
/// AT-024: GET …/{id}/delivery-progress — every line of the draft once, in line order, under the draft read. The answer
/// is read afresh from the delivery owner on every request (nothing is cached or stored in Sales), so a delivery recorded
/// in Logistics shows the next time the order is opened. <c>draftVersion</c> is the concurrency version the lines were
/// read against.
/// </summary>
public sealed record SalesOrderDraftDeliveryProgressResponse(string DraftVersion, IReadOnlyList<SalesDraftLineDeliveryProgressV1> Lines);
