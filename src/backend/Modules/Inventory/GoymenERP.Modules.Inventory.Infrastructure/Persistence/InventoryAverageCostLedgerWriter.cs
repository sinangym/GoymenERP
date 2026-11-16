using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Domain.Costing;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// STK-S35: the only writer of Inventory's moving-average ledger. It stages the next state of one (company, location,
/// product) position, computed by <see cref="AverageCostCalculator"/> (the one deterministic core), in the caller's
/// <see cref="InventoryDbContext"/>; it never saves, so the ledger change commits in the same single save, attempt and
/// audit as the physical movement that caused it, or not at all. A stale concurrent writer is refused at that save by the
/// row version.
/// <para>
/// A new row starts at version 1; a save that changes a stored position bumps its version by one; a position the movement
/// leaves unchanged (an unreadable position stays unreadable) is not rewritten. The movement carries the position's physical stock before it
/// (<see cref="AverageCostMovement.StockOnHandBeforeKg"/>), measured as <see cref="InventoryAverageCostLedgerRows.CostedStock"/>
/// measures it.
/// </para>
/// <para>
/// No movement path calls this writer yet: the purchase price input (SAT-D295, the Purchasing order line) has no port
/// Inventory can read, and recording issues or transfers without the receipts that cost them would only turn positions
/// unreadable for good (<see cref="AverageCostUnreadableReason.LedgerStockMismatch"/> is sticky). The receipt, issue and
/// transfer paths are wired together once that port exists (reported as NEEDS-SINAN / a message to Purchasing).
/// </para>
/// <para>
/// The one caller today is the approved opening cost (SAT-D304, <see cref="StageOpeningCostAsync"/>): it opens a position
/// the ledger never tracked and never changes a stored one.
/// </para>
/// </summary>
internal sealed class InventoryAverageCostLedgerWriter(
    InventoryDbContext dbContext, ICompanyContext companyContext, TimeProvider timeProvider)
{
    /// <summary>Stages <paramref name="movement"/> on the position and returns the position it leaves.</summary>
    public async Task<AverageCostPosition> StageAsync(
        Guid locationId, Guid productId, AverageCostMovement movement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);
        var companyId = RequireCompany();
        RequireIdentity(locationId, productId);
        var row = await TrackedRowAsync(companyId, locationId, productId, cancellationToken);
        var after = AverageCostCalculator.Apply(row is null ? null : InventoryAverageCostLedgerRows.PositionOf(row), movement);
        Stage(row, companyId, locationId, productId, after);
        return after;
    }

    /// <summary>
    /// Stages a transfer of <paramref name="quantityKg"/> from one location to another (SAT-D260: the target receives it at
    /// the source average) and returns both positions it leaves.
    /// </summary>
    public async Task<AverageCostTransferResult> StageTransferAsync(
        Guid sourceLocationId,
        decimal sourceStockOnHandBeforeKg,
        Guid targetLocationId,
        decimal targetStockOnHandBeforeKg,
        Guid productId,
        decimal quantityKg,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        RequireIdentity(sourceLocationId, productId);
        RequireIdentity(targetLocationId, productId);
        if (sourceLocationId == targetLocationId)
        {
            throw new ArgumentException("A transfer moves stock between two locations.", nameof(targetLocationId));
        }

        var sourceRow = await TrackedRowAsync(companyId, sourceLocationId, productId, cancellationToken);
        var targetRow = await TrackedRowAsync(companyId, targetLocationId, productId, cancellationToken);
        var result = AverageCostCalculator.ApplyTransfer(
            sourceRow is null ? null : InventoryAverageCostLedgerRows.PositionOf(sourceRow), sourceStockOnHandBeforeKg,
            targetRow is null ? null : InventoryAverageCostLedgerRows.PositionOf(targetRow), targetStockOnHandBeforeKg,
            quantityKg);
        Stage(sourceRow, companyId, sourceLocationId, productId, result.Source);
        Stage(targetRow, companyId, targetLocationId, productId, result.Target);
        return result;
    }

    /// <summary>
    /// SAT-D304: stages the approved opening cost of a position the ledger never tracked, costing
    /// <paramref name="stockOnHandKg"/> (measured as <see cref="InventoryAverageCostLedgerRows.CostedStock"/> measures it)
    /// at <paramref name="unitCostPerStockKg"/>, and returns the new row's id and position. A position with any ledger
    /// state, staged or stored, is refused by the core (<see cref="AverageCostCalculator.ApplyOpeningCost"/>); the
    /// caller checks first, and the unique position index refuses a concurrent opener at the save.
    /// </summary>
    public async Task<(Guid PositionId, AverageCostPosition Position)> StageOpeningCostAsync(
        Guid locationId,
        Guid productId,
        decimal stockOnHandKg,
        decimal unitCostPerStockKg,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        RequireIdentity(locationId, productId);
        var row = await TrackedRowAsync(companyId, locationId, productId, cancellationToken);
        var after = AverageCostCalculator.ApplyOpeningCost(
            row is null ? null : InventoryAverageCostLedgerRows.PositionOf(row), stockOnHandKg, unitCostPerStockKg);
        Stage(row, companyId, locationId, productId, after);
        var staged = dbContext.AverageCostLedger.Local.Single(candidate => candidate.CompanyId == companyId
            && candidate.LocationId == locationId && candidate.ProductId == productId);
        return (staged.Id, after);
    }

    private Guid RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Maliyet defteri yalnız çözümlenmiş şirket bağlamında yazılır.");
        }

        return companyContext.CompanyId;
    }

    private static void RequireIdentity(Guid locationId, Guid productId)
    {
        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("The location is named.", nameof(locationId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("The product is named.", nameof(productId));
        }
    }

    /// <summary>
    /// The position's row as this unit of work sees it: a row staged earlier in the same save first (it is not in the
    /// database yet), otherwise the stored row, tracked so its row version guards the save.
    /// </summary>
    private async Task<AverageCostLedgerRow?> TrackedRowAsync(
        Guid companyId, Guid locationId, Guid productId, CancellationToken cancellationToken)
    {
        var staged = dbContext.AverageCostLedger.Local.SingleOrDefault(row => row.CompanyId == companyId
            && row.LocationId == locationId && row.ProductId == productId);
        if (staged is not null)
        {
            return staged;
        }

        return await dbContext.AverageCostLedger
            .Where(row => row.CompanyId == companyId && row.LocationId == locationId && row.ProductId == productId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private void Stage(AverageCostLedgerRow? row, Guid companyId, Guid locationId, Guid productId, AverageCostPosition after)
    {
        var now = timeProvider.GetUtcNow();
        if (row is null)
        {
            row = new AverageCostLedgerRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                LocationId = locationId,
                ProductId = productId,
                Version = 1,
                UpdatedAtUtc = now,
            };
            InventoryAverageCostLedgerRows.Write(row, after);
            dbContext.AverageCostLedger.Add(row);
            return;
        }

        if (InventoryAverageCostLedgerRows.PositionOf(row) == after)
        {
            return;
        }

        // One save moves a row's version by one at most: a row added or already changed in this unit of work keeps the
        // version that save will store.
        var firstChangeInThisSave = dbContext.Entry(row).State == EntityState.Unchanged;
        InventoryAverageCostLedgerRows.Write(row, after);
        if (firstChangeInThisSave)
        {
            row.Version = checked(row.Version + 1);
        }

        row.UpdatedAtUtc = now;
    }
}
