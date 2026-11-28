using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Server-only participant input, deliberately absent from CreatePurchaseOrderRequest and its legacy hash.
/// Actual owner records must be read inside the future parent-owned command transaction.</summary>
public sealed record PurchaseMixedOrderBindingContextV1(StoredPurchaseOrder Parent,
    IReadOnlyList<PurchaseServiceOrderState> Services, IReadOnlyList<PurchaseOrderServiceSelection> Selections);

/// <summary>No production implementation is registered. Never invoke a second public service-order command here.
/// The implementation must use OrdersApprove, native fresh supplier/reference/tax checks, both maker/version guards,
/// one owned transaction and the real scoped audit writer. Native command/hash/replay admission is a later increment.</summary>
public interface IPurchaseMixedOrderApprovalParticipantV1
{
    Task<IReadOnlyList<PurchaseServiceOrderState>> ApproveWithinOwnerAsync(PurchaseMixedOrderBindingContextV1 context,
        long expectedParentVersion, PurchasingWriteEnvelope envelope, DateTimeOffset now,
        Func<CancellationToken, Task> freshOrdersApproveFence, CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<PurchaseServiceOrderState>>(new PurchasingServiceException(503, "service_unavailable"));
}
