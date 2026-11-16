namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// AT-024: one order line's delivered and remaining quantity, exact decimal text in the line's order unit (Ö2, never
/// rounded). <c>ordered</c> is the line's quantity; <c>shipped</c> and <c>delivered</c> are the delivery owner's recorded
/// totals (Logistics, through <see cref="ISalesOrderDeliveryReadPortV1"/>); <c>remaining</c> = ordered − shipped, never
/// below zero; <c>excess</c> = shipped − ordered when more was shipped than ordered, else zero (KLN-D100 + KLN-D106: the order's
/// delivered quantity counts at the shipment's stock-out approval, so the shipped total is the delivered counter of the open
/// quantity; <c>delivered</c> stays the owner's separate delivery record, displayed only). These are facts only:
/// whether a shortfall closes the line or an excess is acceptable is the over/under-delivery tolerance policy (OPEN-004,
/// not decided), so no tolerance, warning or completion state is derived here.
/// <para>
/// <c>shippedBy</c> (F-006, B-33; SAT-D216/SAT-D222): who recorded the definite shipments counted in <c>shipped</c>, as the
/// delivery owner names them through <see cref="SalesOrderLineDeliveryV1.ShippedBy"/> — every named actor once, trimmed, in
/// ordinal order, in the Sales actor id format. An empty list when nothing definite is shipped (<c>shipped</c> is zero);
/// <c>null</c> when something is shipped but the owner does not name every shipper of it (a shipment recorded before the
/// owner kept its actor, or a blank or over-long name) — the same "unverified" reading the over-delivery approval and the
/// line closure refuse on (fail-closed), never only the known part. A display fact: it grants and judges nothing.
/// </para>
/// </summary>
public sealed record SalesDraftLineDeliveryProgressV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Ordered,
    string Shipped,
    string Delivered,
    string Remaining,
    string Excess,
    IReadOnlyList<string>? ShippedBy);

/// <summary>
/// AT-024: GET …/{id}/delivery-progress — every line of the draft once, in line order, under the draft read. The answer
/// is read afresh from the delivery owner on every request (nothing is cached or stored in Sales), so a delivery recorded
/// in Logistics shows the next time the order is opened. <c>draftVersion</c> is the concurrency version the lines were
/// read against.
/// </summary>
public sealed record SalesOrderDraftDeliveryProgressResponse(string DraftVersion, IReadOnlyList<SalesDraftLineDeliveryProgressV1> Lines);
