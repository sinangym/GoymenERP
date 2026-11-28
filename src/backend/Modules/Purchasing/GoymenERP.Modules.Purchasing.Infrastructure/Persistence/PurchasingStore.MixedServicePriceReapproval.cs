using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    internal Task<StoredPurchaseOrder?> ReadBoundServicePriceParentAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token) =>
        ReadMixedParentAsync(id, fence, token);

    private async Task<PurchaseMixedServicePriceReceiptV1?> ReadServicePriceReapprovalContextAsync(StoredPurchaseOrder parent,
        IReadOnlyList<PurchaseServiceOrderState> children, Func<CancellationToken, Task> fence, CancellationToken token)
    {
        var revision = parent.Order.ServicePriceRevisions.LastOrDefault();
        if (revision is null || parent.Order.SupplierRevisions.LastOrDefault()?.OrderVersionAfter > revision.OrderVersionAfter)
        {
            if (children.Any(x => x.Status != PurchaseServiceOrderStatus.Draft)) throw MixedSupplierUnavailable();
            return null;
        }
        var receipt = await ReadMixedServicePriceVersionAsync(parent.Order.Id, revision.OrderVersionAfter, fence, token, parent.Order.Version);
        if (receipt.NativeRevision != revision || receipt.CompanyId != parent.Order.CompanyId || receipt.ResultVersion > parent.Order.Version
            || receipt.Visible.Order.SupplierPartyId != parent.Order.SupplierPartyId || receipt.Visible.Order.CompanyAccountId != parent.CompanyAccountId)
            throw MixedSupplierUnavailable();
        foreach (var child in children.Where(x => x.Status == PurchaseServiceOrderStatus.Approved))
        {
            var original = receipt.Children.SingleOrDefault(x => x.After.OrderId == child.OrderId)?.After;
            if (original is null || !HasNativeApprovalState(child, original)) throw MixedSupplierUnavailable();
        }
        return receipt;
    }
}
