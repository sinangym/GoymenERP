namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Purchasing's already-authorized stored order read seam; no Cari data is present here.</summary>
public interface IPurchasingOrderRecordReadSourceV1
{
    Task<PurchaseOrderDetailView> GetOrderDetailAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<PurchaseOrderListResponse> GetOrderListAsync(
        PurchaseOrderListQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// List/detail projection that requires an exact Cari display for every supplier id extracted from
/// the already-authorized Purchasing records. It never accepts a caller-supplied Party id.
/// </summary>
public interface IPurchasingOrderReadServiceV1 : IPurchasingOrderRecordReadSourceV1;
