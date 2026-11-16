using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed class InventoryFutureAllocationQueryService(InventoryDbContext database, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority) : IInventoryFutureAllocationQueryV1
{
    public async Task<InventoryFutureAllocationListV1> ListByDemandAsync(Guid salesOrderId, Guid salesOrderLineId,
        CancellationToken cancellationToken = default)
    {
        var fence = new InventoryFutureAllocationUserFence(company, user, authority, ErpPermissions.InventoryFutureAllocationRead);
        await fence.RequireStillAsync(cancellationToken);
        if (salesOrderId == Guid.Empty || salesOrderLineId == Guid.Empty)
            throw new InventoryAllocationException(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        InventoryFutureAllocationViewV1[] items;
        try
        {
            var rows = await database.FutureSupplyAllocations.AsNoTracking()
                .Where(row => row.DemandId == salesOrderId && row.DemandLineId == salesOrderLineId)
                .OrderByDescending(row => row.CreatedAtUtc).ThenBy(row => row.Id)
                .Take(InventoryFutureAllocationApiV1.ReadLimit + 1).ToListAsync(cancellationToken);
            items = rows.Select(InventoryFutureAllocationEngine.View).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            await fence.RequireStillAsync(cancellationToken);
            throw new InventoryAllocationException(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, innerException: exception);
        }
        await fence.RequireStillAsync(cancellationToken);
        if (items.Any(item => item.CompanyId != fence.CompanyId))
            throw new InventoryAllocationException(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        return new(fence.CompanyId, salesOrderId, salesOrderLineId,
            items.Take(InventoryFutureAllocationApiV1.ReadLimit).ToArray(), items.Length > InventoryFutureAllocationApiV1.ReadLimit);
    }
}
