namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>Read-only metadata for choosing a real invoice of the current company's order; never exposes prices.</summary>
public sealed record SalesSubcontractInvoiceListItemV1(Guid Id, string DocumentNo, string State, string Version,
    DateOnly BusinessDate, DateTimeOffset CreatedAtUtc);
public sealed record SalesSubcontractInvoiceListV1(Guid SalesOrderId, IReadOnlyList<SalesSubcontractInvoiceListItemV1> Items,
    string? NextCursor);
public interface ISalesSubcontractInvoiceListStoreV1
{
    Task<SalesSubcontractInvoiceListV1> ListSubcontractInvoicesAsync(Guid salesOrderId, int pageSize, string? cursor,
        CancellationToken cancellationToken = default);
}
public interface ISalesSubcontractInvoiceListServiceV1
{
    Task<SalesSubcontractInvoiceListV1> ListSubcontractInvoicesAsync(Guid salesOrderId, SalesInvoiceListQuery query,
        CancellationToken cancellationToken = default);
}
