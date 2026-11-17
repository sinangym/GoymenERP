using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesInvoiceService : ISalesSubcontractInvoiceListServiceV1
{
    public async Task<SalesSubcontractInvoiceListV1> ListSubcontractInvoicesAsync(Guid salesOrderId, SalesInvoiceListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        // SAT-D238: metadata requires the existing invoice-read permission, never a write or price-read grant.
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        if (salesOrderId == Guid.Empty) throw new SalesDraftServiceException(404, "not_found");
        if (query.PageSize is < 1 or > SalesInvoiceApiV1.MaximumPageSize || query.Cursor is { Length: 0 or > MaximumCursorLength })
            throw new SalesDraftServiceException(400, "validation_failed");
        var order = await StoreAsync(scope, () => orders.FindDraftAsync(salesOrderId, cancellationToken), cancellationToken, ReadPermission);
        if (order is null || !order.IsSubcontractService) throw new SalesDraftServiceException(404, "not_found");
        var store = subcontractInvoiceList ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var result = await StoreAsync(scope, () => store.ListSubcontractInvoicesAsync(salesOrderId, query.PageSize, query.Cursor,
            cancellationToken), cancellationToken, ReadPermission);
        await RequireStillAsync(scope, ReadPermission, cancellationToken);
        if (result is null || result.SalesOrderId != salesOrderId || result.Items is null || result.Items.Count > query.PageSize)
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        return result;
    }
}
