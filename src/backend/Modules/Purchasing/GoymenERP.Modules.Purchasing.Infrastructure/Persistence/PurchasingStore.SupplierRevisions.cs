using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore : IPurchasingSupplierRevisionStoreV1
{
    public Task<PurchasingOrderStoreResult?> ReviseSupplierAsync(Guid orderId, long expectedVersion, Guid partyId, Guid companyAccountId,
        string reason, PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requireStillAuthorized);
        if (companyAccountId == Guid.Empty) throw new ArgumentException("Cari şirket hesabı gerekli.", nameof(companyAccountId));
        var invoiceHistory = PurchaseOrderInvoiceHistory.Unavailable;
        return MutateOrderCoreAsync(envelope, requestHash, PurchasingCommands.OrderSupplierReviseV1, orderId,
            (order, now) => order.ReviseSupplier(expectedVersion, partyId, invoiceHistory, envelope.ActorId.Trim(), reason, now),
            null, new PurchasingMutationAudit("purchasing.purchase-order.supplier-revised.v1", "ReviseSupplier", reason), cancellationToken,
            async (_, token) =>
            {
                invoiceHistory = await dbContext.InvoiceOrderBindings.AsNoTracking().AnyAsync(item => item.OrderId == orderId, token)
                    ? PurchaseOrderInvoiceHistory.Exists : PurchaseOrderInvoiceHistory.None;
                await requireStillAuthorized(token);
            }, companyAccountId, requireStillAuthorized);
    }

    public Task<PurchasingOrderStoreResult?> SetRevisionPriceAsync(Guid orderId, Guid lineId, long expectedVersion, PurchaseOrderLinePrice price,
        PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requireStillAuthorized);
        return MutateOrderCoreAsync(envelope, requestHash, PurchasingCommands.OrderSupplierRevisionPriceV1, orderId,
            (order, now) => order.SetSupplierRevisionPrice(expectedVersion, lineId, price, envelope.ActorId.Trim(), now),
            null, new PurchasingMutationAudit("purchasing.purchase-order.supplier-revision-price-recorded.v1", "SetRevisionPrice", "Yeni cari için fiyat girildi"),
            cancellationToken, null, null, requireStillAuthorized);
    }
}
