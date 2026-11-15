namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>
/// One location's share of the company-wide average: Inventory's company-owned physical stock of the product at the
/// location (<see cref="OnHandKg"/>) and the location's moving-average position (<c>null</c> when the ledger never saw it).
/// </summary>
public sealed record CompanyWideAverageCostLocation(Guid LocationId, decimal OnHandKg, AverageCostPosition? Position);

public enum CompanyWideAverageCostStatus
{
    /// <summary>Every location with stock has a readable average that covers its whole stock.</summary>
    Known = 1,

    /// <summary>At least one location with stock cannot be read. No partial average is given (SAT-D347, SAT-D350).</summary>
    Unreadable = 2,

    /// <summary>No location holds stock of the product, so no location is a cost source (SAT-D347 implementation detail).</summary>
    NoSource = 3,
}

/// <summary>
/// The company-wide answer. <see cref="Included"/> are the locations with stock, ordered by location id; they are the
/// weight of a known figure and the place a refusal was found. <see cref="OnHandKg"/> is their total stock.
/// </summary>
public sealed record CompanyWideAverageCostResult(
    CompanyWideAverageCostStatus Status,
    decimal? AverageCostPerKg,
    AverageCostUnreadableReason? UnreadableReason,
    Guid? UnreadableLocationId,
    decimal OnHandKg,
    IReadOnlyList<CompanyWideAverageCostLocation> Included);

/// <summary>
/// SAT-D272 / SAT-D347 / SAT-D350: the company-wide (all locations) on-hand-kg-weighted moving-average purchase cost of one
/// product. The aggregation rule lives here, in Inventory's own deterministic core; Sales never combines location reads.
/// Pure: no clock, no storage, no company or role.
/// <list type="bullet">
/// <item>The weight is each location's on-hand kilograms. A location without stock is left out of the weight, whatever its
/// ledger says (SAT-D347: a location without a cost source is the stock-0 location).</item>
/// <item>No location with stock: <see cref="CompanyWideAverageCostStatus.NoSource"/> (SAT-D347 implementation detail,
/// reversible).</item>
/// <item>One location with stock that cannot be read makes the whole answer
/// <see cref="CompanyWideAverageCostStatus.Unreadable"/>; no partial average (SAT-D347, SAT-D350). A location with stock but
/// no ledger position, or whose known position covers a different quantity than its stock, holds kilograms the ledger never
/// costed: it reads as <see cref="AverageCostUnreadableReason.LedgerStockMismatch"/> (fail-closed author reading of
/// "a location without a cost source has no stock"; reversible, reported as NEEDS-SINAN). When several locations cannot be
/// read, the first in location-id order names the reason, so the answer is deterministic.</item>
/// <item>Otherwise <c>round4(Σ(kgᵢ·aᵢ) / Σkgᵢ)</c>, rounded once at the internal scale, AwayFromZero (SAT-D265 (2)).</item>
/// </list>
/// </summary>
public static class CompanyWideAverageCostCalculator
{
    public static CompanyWideAverageCostResult Aggregate(IEnumerable<CompanyWideAverageCostLocation> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        var all = locations.ToArray();
        var seen = new HashSet<Guid>();
        foreach (var location in all)
        {
            ArgumentNullException.ThrowIfNull(location, nameof(locations));
            if (location.LocationId == Guid.Empty)
            {
                throw new ArgumentException("Every location is named.", nameof(locations));
            }

            if (!seen.Add(location.LocationId))
            {
                throw new ArgumentException("A location is given once.", nameof(locations));
            }

            if (location.OnHandKg < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(locations), "Physical stock is never negative.");
            }
        }

        var included = all.Where(location => location.OnHandKg > 0m).OrderBy(location => location.LocationId).ToArray();
        var totalKg = included.Sum(location => location.OnHandKg);
        if (included.Length == 0)
        {
            return new(CompanyWideAverageCostStatus.NoSource, null, null, null, 0m, included);
        }

        foreach (var location in included)
        {
            if (ReasonOf(location) is { } reason)
            {
                return new(CompanyWideAverageCostStatus.Unreadable, null, reason, location.LocationId, totalKg, included);
            }
        }

        var weightedCost = included.Sum(location => location.OnHandKg * location.Position!.AverageCostPerKg!.Value);
        var average = AverageCostPrecision.Round(weightedCost / totalKg);
        return new(CompanyWideAverageCostStatus.Known, average, null, null, totalKg, included);
    }

    private static AverageCostUnreadableReason? ReasonOf(CompanyWideAverageCostLocation location) => location.Position switch
    {
        null => AverageCostUnreadableReason.LedgerStockMismatch,
        { Status: AverageCostStatus.Unreadable, UnreadableReason: { } reason } => reason,
        { Status: AverageCostStatus.Known, QuantityKg: { } costedKg, AverageCostPerKg: not null }
            when costedKg == location.OnHandKg => null,
        { Status: AverageCostStatus.Known } => AverageCostUnreadableReason.LedgerStockMismatch,
        _ => throw new ArgumentException("A position is known or unreadable.", nameof(location)),
    };
}
