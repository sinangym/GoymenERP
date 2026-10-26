namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// SAT-D8 / API-10 (delegation No 21, 38): one Sales order line's APPROVED physical customer returns, read from the
/// Logistics owner (through <see cref="ISalesOrderCustomerReturnReadPortV1"/>) in the line's order unit. The value is the
/// owner's exact approved total (Ö2), never converted or rounded; pending or rejected return requests are not counted.
/// A line without an approved return carries <c>"0"</c> only after the owner answered successfully. These are facts only:
/// no credit note, invoice, stock or remaining-quantity effect is derived here (the return invoice is API-40).
/// </summary>
public sealed record SalesDraftLineCustomerReturnV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Ordered,
    string ApprovedReturned);

/// <summary>
/// SAT-D8 / API-10: GET …/{id}/customer-returns — every line of the draft once, in line order, under the draft read
/// (returns live on the order, there is no separate menu). The answer is read afresh from Logistics on every request;
/// nothing is cached or copied into Sales. <c>draftVersion</c> is the concurrency version the lines were read against.
/// </summary>
public sealed record SalesOrderDraftCustomerReturnsResponse(
    string DraftVersion,
    IReadOnlyList<SalesDraftLineCustomerReturnV1> Lines);
