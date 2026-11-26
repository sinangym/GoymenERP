namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesOrderInvoicedQuantityReadOutcomeV1 { Available = 1, Unavailable = 2 }

/// <summary>Exact quantities from current, actually finalized Sales invoice lines; no prices or financial return inference.</summary>
public sealed record SalesOrderInvoicedQuantityLineV1(Guid InvoiceId, Guid InvoiceLineId, Guid SalesOrderLineId,
    Guid UnitId, decimal Quantity, string InvoiceKind, string SourceVersion);

/// <summary>History flag is part of the same bounded source snapshot: null means unknown; true requires separate Finance cancellation proof.</summary>
public sealed record SalesOrderInvoicedQuantityReadV1(Guid CompanyId, Guid SalesOrderId,
    SalesOrderInvoicedQuantityReadOutcomeV1 Outcome, IReadOnlyList<SalesOrderInvoicedQuantityLineV1> Lines,
    string? SourceVersion,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? HasOfficiallyCancelledInvoices = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SalesOrderCancelledInvoiceProofV1>? CancelledInvoices = null);

/// <summary>Retained native finalization and official-cancellation proof. No amount or inference of Finance reversal.</summary>
public sealed record SalesOrderCancelledInvoiceProofV1(Guid CompanyId, Guid InvoiceId, string InvoiceKind,
    Guid AdmissionId, long OriginalFinalizedVersion, long CancellationInvoiceVersion,
    Guid FinalizedEventId, string FinalizedPayloadSha256, Guid CancellationId, Guid CancellationEventId,
    string CancellationPayloadSha256, DateTimeOffset CancelledAtUtc);

/// <summary>Price-free internal purpose read for an authorized order reader. A complete empty source proves zero;
/// missing, changing, ambiguous or legacy evidence is Unavailable. The token is not a later mutation lock.</summary>
public interface ISalesOrderInvoicedQuantityReadPortV1
{
    const string SourceVersionPrefix = "sales.order-invoiced-quantities.v1/";
    Task<SalesOrderInvoicedQuantityReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
