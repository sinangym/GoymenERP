using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>ALS-D112/D115: own invoice history is measured under the same order-line locks used by invoice matching and receipt.</summary>
public interface IPurchasingSupplierRevisionStoreV1
{
    Task<PurchasingOrderStoreResult?> ReviseSupplierAsync(Guid orderId, long expectedVersion, Guid partyId, Guid companyAccountId,
        string reason, PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
        CancellationToken cancellationToken = default);
    Task<PurchasingOrderStoreResult?> SetRevisionPriceAsync(Guid orderId, Guid lineId, long expectedVersion, PurchaseOrderLinePrice price,
        PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
        CancellationToken cancellationToken = default);
}
