using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

internal enum InventoryAllocationDemandCapOutcome
{
    /// <summary>The request fits under the Sales line's open quantity.</summary>
    Fits = 1,

    /// <summary>KLN-D65 (2): physical + future + this request would exceed the open quantity.</summary>
    Exceeds = 2,

    /// <summary>KLN-D66 (3): a kilogram comparison is needed but some quantity cannot be brought to kilograms.</summary>
    ConversionUnavailable = 3,

    /// <summary>The Sales open quantity is not a readable non-negative decimal (fail-closed, 503).</summary>
    Unreadable = 4,
}

/// <summary>What a create or change adds to the Sales line: physical kilograms, or a quantity in a (purchase or request) unit.</summary>
internal sealed record InventoryAllocationDemandCapRequest(decimal? PhysicalKilograms, decimal? Quantity, Guid? UnitId)
{
    public static InventoryAllocationDemandCapRequest Physical(decimal kilograms) => new(kilograms, null, null);

    public static InventoryAllocationDemandCapRequest Future(decimal quantity, Guid unitId) => new(null, quantity, unitId);
}

/// <summary>
/// KLN-D65 / KLN-D66 (F-017 STOK spec §3.2 step 5, §9.1 candidate 3): the ONE total cap both creation paths use — a physical
/// allocation (<see cref="InventoryPhysicalAllocationDemandCapWriter"/>) and a future supply allocation
/// (<see cref="InventoryFutureAllocationWriter"/>). The caller holds the Sales order line lock in its open transaction, so both
/// sides are read under the same lock (KLN-D65 (3)).
/// <para>
/// Σ active physical remaining kilograms + Σ active future remaining + this request ≤ the line's open quantity (KLN-D28 (4)). When
/// every quantity is already in the Sales order unit (no physical kilograms held or asked, every future unit = the order unit) the
/// comparison is exact in that unit and needs no conversion. Otherwise every side is brought to kilograms (KLN-D66 (1)) through
/// <see cref="IInventoryFutureUnitConverterV1"/> — never re-implemented here — and any missing conversion is
/// <see cref="InventoryAllocationDemandCapOutcome.ConversionUnavailable"/>.
/// </para>
/// </summary>
internal static class InventoryAllocationDemandCap
{
    /// <param name="replacedPhysicalKilograms">
    /// A change replaces the allocation's current remaining kilograms with the request (resize in place, or move: the source is
    /// released in full); that part is taken out of the held physical total. Zero for a create.
    /// </param>
    public static async Task<InventoryAllocationDemandCapOutcome> CheckAsync(
        InventoryDbContext dbContext,
        IInventoryFutureUnitConverterV1 converter,
        InventoryFutureDemandLineV1 demand,
        InventoryAllocationDemandCapRequest request,
        decimal replacedPhysicalKilograms,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(request);
        if (!decimal.TryParse(demand.OpenQuantityText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var open) || open < 0m)
        {
            return InventoryAllocationDemandCapOutcome.Unreadable;
        }

        var heldPhysical = await dbContext.Allocations.AsNoTracking()
            .Where(row => row.DemandKind == InventoryAllocationDemandKindsV1.SalesOrderLine
                && row.DemandId == demand.OrderId && row.DemandLineId == demand.LineId
                && row.Status == (int)StockAllocationStatus.Active)
            .SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
        var physical = heldPhysical - replacedPhysicalKilograms + (request.PhysicalKilograms ?? 0m);
        if (physical < 0m)
        {
            // The replaced part is always one of the held rows; a negative total means the rows changed under the lock.
            return InventoryAllocationDemandCapOutcome.Unreadable;
        }

        var futureByUnit = await dbContext.FutureSupplyAllocations.AsNoTracking()
            .Where(row => row.DemandKind == InventoryFutureAllocationKindsV1.SalesOrderLine
                && row.DemandId == demand.OrderId && row.DemandLineId == demand.LineId
                && row.Status == (int)FutureSupplyAllocationStatus.Active)
            .GroupBy(row => row.PurchaseUnitId)
            .Select(group => new { UnitId = group.Key, Remaining = group.Sum(row => row.RemainingQuantity) })
            .ToListAsync(cancellationToken);
        var futures = new List<(Guid UnitId, decimal Remaining)>();
        futures.AddRange(futureByUnit.Select(item => (item.UnitId, item.Remaining)));
        if (request.Quantity is { } asked && request.UnitId is { } askedUnit)
        {
            futures.Add((askedUnit, asked));
        }

        futures.Sort((left, right) => left.UnitId.CompareTo(right.UnitId));

        if (physical == 0m && futures.All(item => item.UnitId == demand.UnitId))
        {
            // Exact in the Sales order unit (Sales may keep more decimals than a purchase line): no conversion is needed.
            return futures.Sum(item => item.Remaining) > open
                ? InventoryAllocationDemandCapOutcome.Exceeds
                : InventoryAllocationDemandCapOutcome.Fits;
        }

        var openKilograms = await ToKilogramsAsync(converter, demand, open, demand.UnitId, cancellationToken);
        if (openKilograms is null)
        {
            return InventoryAllocationDemandCapOutcome.ConversionUnavailable;
        }

        var total = physical;
        foreach (var (unitId, remaining) in futures)
        {
            if (await ToKilogramsAsync(converter, demand, remaining, unitId, cancellationToken) is not { } kilograms)
            {
                return InventoryAllocationDemandCapOutcome.ConversionUnavailable;
            }

            total += kilograms;
        }

        return total > openKilograms.Value
            ? InventoryAllocationDemandCapOutcome.Exceeds
            : InventoryAllocationDemandCapOutcome.Fits;
    }

    private static async Task<decimal?> ToKilogramsAsync(IInventoryFutureUnitConverterV1 converter,
        InventoryFutureDemandLineV1 demand, decimal quantity, Guid unitId, CancellationToken cancellationToken)
    {
        if (quantity == 0m)
        {
            return 0m;
        }

        // KLN-D67: the product identity decides; the card revision handed over is the Sales line's (the converter may ignore it).
        var converted = await converter.ConvertToKilogramsAsync(demand.ProductId, demand.ProductVersion, quantity, unitId,
            cancellationToken);
        return converted.Outcome == InventoryFutureUnitConversionOutcomeV1.Converted && converted.Quantity is { } value
            && value >= 0m
                ? value
                : null;
    }
}
