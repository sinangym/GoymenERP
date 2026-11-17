namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// The immutable source share of a normal <c>sales.invoice.finalized.v1</c> line (SAT-D94, SAT-D424/D425).
/// The invoice claims the whole shipment share; these identities and the invoice quantity are copied from that frozen share.
/// <paramref name="InvoicedQuantity"/> is the stored invoice quantity text in <paramref name="UnitId"/>, not proof of the
/// Inventory issue's physical kilograms or its cost. The Inventory owner resolves that separate evidence by the shipment.
/// Previously recorded events are not enriched or rewritten.
/// </summary>
public sealed record SalesInvoiceShipmentSourceV1(
    Guid ShipmentId,
    Guid DispatchNoteId,
    int DispatchNoteLineNo,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    int PriceBasisRevision,
    Guid ProductId,
    Guid UnitId,
    string InvoicedQuantity);
