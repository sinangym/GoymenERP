using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-009 D5 / URT-D34: the Inventory-owned answer to <see cref="IInventoryFreeQuantityReadPortV1"/>. Same company
/// fence and fail-closed reconciliation as the disposition port (it is the same computation); untracked and
/// side-effect free. Never the decision source: the allocation command re-checks inside its own transaction.
/// </summary>
internal sealed class InventoryFreeQuantityReadPort(InventoryDbContext dbContext, ICompanyContext companyContext)
    : IInventoryFreeQuantityReadPortV1
{
    public async Task<InventoryFreeQuantityReadV1> GetAsync(
        Guid companyId, Guid labelId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Serbest miktar yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (companyId != companyContext.CompanyId)
        {
            return InventoryFreeQuantityReadV1.NotFound;
        }

        var snapshot = await InventoryLabelDispositionReader.ReadAsync(dbContext, companyId, labelId,
            cancellationToken);
        return snapshot.Status switch
        {
            InventoryLabelDispositionStatusV1.Value => new InventoryFreeQuantityReadV1(
                InventoryLabelDispositionStatusV1.Value,
                new InventoryFreeQuantityV1(companyId, snapshot.LabelId, snapshot.ProductId,
                    snapshot.LabelVersion.ToString(CultureInfo.InvariantCulture),
                    InventoryManufacturingPlanAllocationContractV1.Format(snapshot.FreeUnallocatedKg),
                    snapshot.FreePositionCount)),
            InventoryLabelDispositionStatusV1.NotFound => InventoryFreeQuantityReadV1.NotFound,
            _ => InventoryFreeQuantityReadV1.Unreadable,
        };
    }
}
