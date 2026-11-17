namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesOrderInvoicedQuantityReadOutcomeV1 { Available = 1, Unavailable = 2 }

/// <summary>Exact quantities from current, actually finalized Sales invoice lines; no prices or financial return inference.</summary>
public sealed record SalesOrderInvoicedQuantityLineV1(Guid InvoiceId, Guid InvoiceLineId, Guid SalesOrderLineId,
    Guid UnitId, decimal Quantity, string InvoiceKind, string SourceVersion);

public sealed record SalesOrderInvoicedQuantityReadV1(Guid CompanyId, Guid SalesOrderId,
    SalesOrderInvoicedQuantityReadOutcomeV1 Outcome, IReadOnlyList<SalesOrderInvoicedQuantityLineV1> Lines,
    string? SourceVersion);

/// <summary>Price-free internal purpose read for an authorized order reader. A complete empty source proves zero;
/// missing, changing, ambiguous or legacy evidence is Unavailable. The token is not a later mutation lock.</summary>
public interface ISalesOrderInvoicedQuantityReadPortV1
{
    const string SourceVersionPrefix = "sales.order-invoiced-quantities.v1/";
    Task<SalesOrderInvoicedQuantityReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
