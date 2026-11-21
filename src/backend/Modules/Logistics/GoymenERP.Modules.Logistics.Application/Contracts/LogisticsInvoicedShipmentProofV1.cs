namespace GoymenERP.Modules.Logistics.Application.Contracts;

public enum LogisticsInvoicedShipmentProofOutcomeV1 { Found = 1, NotFound = 2, Unavailable = 3 }

/// <summary>Price-free owner evidence joining one whole approved dispatch line to its actual loading line and shipment.
/// DispatchQuantity is the recorded source quantity, not an Inventory kilogram allocation or a cost calculation.</summary>
public sealed record LogisticsInvoicedShipmentProofV1(Guid CompanyId, Guid DispatchNoteId, int DispatchNoteLineNo,
    Guid ShipmentId, Guid InstructionId, Guid InstructionLineId, Guid SalesOrderId, Guid SalesOrderLineId,
    int SourceOrderRevision, Guid ProductId, Guid UnitId, decimal DispatchQuantity, string SourceVersion);

public sealed record LogisticsInvoicedShipmentProofReadV1(LogisticsInvoicedShipmentProofOutcomeV1 Outcome,
    LogisticsInvoicedShipmentProofV1? Proof = null);

/// <summary>Internal, company-scoped evidence for an admitted Accounting source consumer. It grants no user authority,
/// exposes no browser route and does not replace the separate user-authorized dispatch invoice picker.</summary>
public interface ILogisticsInvoicedShipmentProofReadPortV1
{
    Task<LogisticsInvoicedShipmentProofReadV1> GetAsync(Guid companyId, Guid dispatchNoteId, int dispatchNoteLineNo,
        Guid shipmentId, CancellationToken cancellationToken = default);
}
