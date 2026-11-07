using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// B-01 item 4: a Manufacturing plan's allocations in every status, so the plan view shows what is held and the order
/// close shows what was consumed and what was released. Company-filtered and untracked; it carries no authority — the
/// Manufacturing caller checks its own permission around the call — and writes nothing.
/// </summary>
public sealed class InventoryManufacturingPlanAllocationReadPort(InventoryDbContext dbContext,
    ICompanyContext companyContext) : IInventoryManufacturingPlanAllocationReadPortV1
{
    public async Task<IReadOnlyList<InventoryManufacturingPlanAllocationViewV1>> GetForPlanAsync(Guid planId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Plan tahsis okuması yalnız çözümlenmiş şirket bağlamında yapılır.");
        }

        if (planId == Guid.Empty)
        {
            return [];
        }

        var companyId = companyContext.CompanyId;
        var rows = await dbContext.Allocations.AsNoTracking()
            .Where(row => row.CompanyId == companyId
                && row.DemandKind == InventoryAllocationDemandKindsV1.ManufacturingPlanInput
                && row.DemandId == planId)
            .OrderBy(row => row.LabelId).ThenBy(row => row.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new InventoryManufacturingPlanAllocationViewV1(
            row.Id,
            planId,
            row.LabelId,
            InventoryManufacturingPlanAllocationContractV1.Format(row.AllocatedKg),
            InventoryManufacturingPlanAllocationContractV1.Format(row.RemainingKg),
            InventoryManufacturingPlanAllocationContractV1.Format(ConsumedKg(row)),
            (InventoryManufacturingPlanAllocationStatusV1)row.Status,
            row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row.ReleaseReason))];
    }

    /// <summary>
    /// A release returns the remaining kg to free stock and zeroes the row's remaining, so the row keeps the released kg
    /// itself: consumed = allocated − remaining − released.
    /// </summary>
    private static decimal ConsumedKg(AllocationRow row) => row.AllocatedKg - row.RemainingKg - (row.ReleasedKg ?? 0m);
}
