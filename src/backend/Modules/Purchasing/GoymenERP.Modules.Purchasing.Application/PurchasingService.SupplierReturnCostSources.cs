using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    public async Task<PurchaseSupplierReturnCostSourcesV1> GetSupplierReturnCostSourcesAsync(Guid orderId, Guid goodsAcceptanceId,
        CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || goodsAcceptanceId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        var returns = returnStore ?? throw ReturnUnavailable();
        _ = await GuardedAsync(scope, permission, () => returns.FindAcceptanceLineAsync(orderId, goodsAcceptanceId, cancellationToken),
            cancellationToken) ?? throw new PurchasingServiceException(404, "not_found");
        return await ReadReturnCostSourcesAsync(scope, permission, orderId, goodsAcceptanceId, cancellationToken);
    }

    private async Task<PurchaseSupplierReturnCostSourcesV1> ReadReturnCostSourcesAsync(Scope scope, string? permission,
        Guid orderId, Guid acceptanceId, CancellationToken cancellationToken)
    {
        var reader = returnCostSources ?? throw ReturnUnavailable();
        var source = await GuardedAsync(scope, permission,
            () => reader.GetAsync(scope.CompanyId, orderId, acceptanceId, cancellationToken), cancellationToken);
        if (source is null || source.CompanyId != scope.CompanyId || source.GoodsAcceptanceId != acceptanceId
            || source.LabelId == Guid.Empty || string.IsNullOrWhiteSpace(source.SourceVersion)) throw ReturnUnavailable();
        await RequireStillAsync(scope, permission, cancellationToken);
        return source;
    }

    private async Task<IReadOnlyList<PurchaseSupplierReturnInvoiceSourceLabelV1>?> ValidateReturnCostSourcesAsync(Scope scope, string? permission, Guid orderId, Guid acceptanceId,
        string kg, string? undo, PurchaseSupplierReturnCostAllocationV1? allocation, CancellationToken cancellationToken)
    {
        if (returnCostSources is null && allocation is null) return null;
        var source = await ReadReturnCostSourcesAsync(scope, permission, orderId, acceptanceId, cancellationToken);
        if (allocation is not null)
        {
            PurchaseSupplierReturnCostAllocationContractV1.ValidateAvailability(allocation, kg, undo!, source);
            return allocation.InvoicedSources.Select(selected => source.InvoicedSources.Single(row => row.SourceEventId == selected.SourceEventId))
                .Select(row => new PurchaseSupplierReturnInvoiceSourceLabelV1(row.SourceEventId, row.InvoiceId, row.InvoiceLineId,
                    row.InvoiceNumber, row.InvoiceLineNo)).ToArray();
        }
        else if (source.InvoicedSources.Count > 0 && undo is null || source.InvoicedSources.Count > 1 || source.InvoicedSources.Count > 0
            && PurchaseSupplierReturnCostAllocationContractV1.HasPositiveQuantity(source.PendingQuantityKg))
            throw new PurchasingServiceException(409, PurchaseSupplierReturnCostAllocationContractV1.Required,
                [new("costAllocation", PurchaseSupplierReturnCostAllocationContractV1.Required)]);
        return null;
    }
}
