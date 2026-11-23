using System.Data;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>ALS-D112/D115: own invoice history is measured under the same order-line locks used by invoice matching and receipt.
/// <c>allocationGuard</c> (F-017 §3.4): runs inside the revision's transaction while every general line lock is held; the service
/// passes the Inventory future-allocation check there, so an order whose lines still carry an active allocation never returns to
/// draft (a future allocation lives only on an APPROVED purchase line).</summary>
public interface IPurchasingSupplierRevisionStoreV1
{
    Task<PurchasingOrderStoreResult?> ReviseSupplierAsync(Guid orderId, long expectedVersion, Guid partyId, Guid companyAccountId,
        string reason, PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
        CancellationToken cancellationToken = default, Func<PurchaseOrder, IDbTransaction, CancellationToken, Task>? allocationGuard = null);
    Task<PurchasingOrderStoreResult?> SetRevisionPriceAsync(Guid orderId, Guid lineId, long expectedVersion, PurchaseOrderLinePrice price,
        PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
        CancellationToken cancellationToken = default);
}
