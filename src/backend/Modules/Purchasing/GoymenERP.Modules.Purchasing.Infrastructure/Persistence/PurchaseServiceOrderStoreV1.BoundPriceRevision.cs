using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchaseServiceOrderStoreV1
{
    private async Task RequireBoundPriceRevisionDraftAsync(Guid orderId, PurchaseServiceOrderState? before,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        var member = await PurchasingStore.ReadMixedOwnerAsync(() => database.OrderServiceMemberships.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ServiceOrderId == orderId, token), fence, token);
        if (member is null) return; // The existing standalone native path remains unchanged.
        var parent = await new PurchasingStore(database, company, audit, TimeProvider.System, decisionAuditWriter: auditWriter)
            .ReadBoundServicePriceParentAsync(member.OrderId, fence, token);
        if (before is null || parent is null || parent.Order.CompanyId != before.CompanyId || parent.Order.Status != PurchaseOrderStatus.Draft
            || parent.Order.ServicePriceRevisions.Count == 0 || parent.Order.ServiceMemberships.Count(x => x.ServiceOrderId == orderId) != 1
            || parent.Order.SupplierPartyId != before.SupplierPartyId || parent.CompanyAccountId != before.CompanyAccountId)
            throw new PurchasingServiceException(503, "service_unavailable");
        // This existing Draft edit does not approve anything. Current child versions/full lines are frozen again at parent preview/approval.
    }
}
