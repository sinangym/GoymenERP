using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchaseServiceOrderStoreV1
{
    public async Task<PurchaseServiceSupplierRevisionFactsV1?> ReadSupplierRevisionFactsAsync(Guid orderId,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(fence); token.ThrowIfCancellationRequested();
        var scope = Company();
        if (database.Database.CurrentTransaction is null || !database.Database.IsRelational())
            throw new PurchasingServiceException(503, "service_unavailable");
        var original = await ReadOwnerAsync(orderId, fence, token);
        if (original is null) return null;
        await fence(token); token.ThrowIfCancellationRequested();
        var exists = await database.ServiceInvoiceBindings.AsNoTracking().AnyAsync(x => x.CompanyId == scope && x.OrderId == orderId, token);
        await fence(token); token.ThrowIfCancellationRequested();
        if (Company() != scope) throw new PurchasingServiceException(403, "forbidden");
        return new(original, exists ? PurchaseOrderInvoiceHistory.Exists : PurchaseOrderInvoiceHistory.None);
    }
}
