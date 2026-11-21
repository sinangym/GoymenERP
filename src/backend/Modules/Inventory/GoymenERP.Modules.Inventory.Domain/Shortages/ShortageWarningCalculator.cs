namespace GoymenERP.Modules.Inventory.Domain.Shortages;

/// <summary>
/// One open Sales order line of the product, in kilograms. <see cref="OpenKg"/> is what is still to ship;
/// <see cref="ReservedKg"/> is what active physical allocations already hold for this line (it is outside free stock,
/// so it is netted from the line instead of being counted twice). A line without <see cref="DueDate"/> is never late.
/// </summary>
public sealed record ShortageDemandLine(
    Guid OrderId,
    Guid LineId,
    string DocumentNo,
    int LineNo,
    DateOnly? DueDate,
    decimal OpenKg,
    decimal ReservedKg);

/// <summary>
/// One open purchase order line of the product, in kilograms. <see cref="ExpectedDate"/> is the purchase delivery date
/// when the Purchasing owner answers one; <c>null</c> means the date is unknown (never assumed on time).
/// </summary>
public sealed record ShortageSupplyLine(
    Guid OrderId,
    Guid LineId,
    DateOnly? ExpectedDate,
    decimal RemainingKg);

/// <summary>The figures of one product: free unreserved stock now, its open demand and its open purchase supply.</summary>
public sealed record ShortageProductInput(
    Guid ProductId,
    decimal FreeStockKg,
    IReadOnlyList<ShortageDemandLine> Demand,
    IReadOnlyList<ShortageSupplyLine> Supply);

public enum ShortageLineCoverage
{
    /// <summary>Covered by reservation, free stock, or purchases dated on or before the due date.</summary>
    Covered = 1,

    /// <summary>Covered only with a purchase whose date is unknown, so lateness cannot be told.</summary>
    CoverageDateUnknown = 2,

    /// <summary>Covered, but a purchase used for it arrives after the line's due date ("geç karşılanıyor").</summary>
    LateCovered = 3,

    /// <summary>Free stock and open purchases do not cover the line.</summary>
    Uncovered = 4,
}

public enum ShortageProductStatus
{
    Covered = 1,
    CoverageDateUnknown = 2,
    LateCovered = 3,

    /// <summary>KLN-D42 (1): net open sales kg is above free stock plus open purchase kg.</summary>
    Shortage = 4,
}

public sealed record ShortageLineResult(
    ShortageDemandLine Line,
    decimal NetKg,
    decimal FromStockKg,
    decimal FromSupplyKg,
    decimal UncoveredKg,
    ShortageLineCoverage Coverage,
    DateOnly? LatestSupplyDate);

public sealed record ShortageProductResult(
    Guid ProductId,
    ShortageProductStatus Status,
    decimal OpenDemandKg,
    decimal ReservedKg,
    decimal NetDemandKg,
    decimal FreeStockKg,
    decimal OpenSupplyKg,
    decimal ShortageKg,
    DateOnly? EarliestDueDate,
    IReadOnlyList<ShortageLineResult> Lines);

/// <summary>
/// KLN-D42: the one deterministic dated-shortage calculator (it replaces minimum stock). Per product it warns when the net
/// open sales kilograms exceed free stock plus open purchase kilograms (rule 1), and it marks a line "late covered" when a
/// purchase it needs is dated after the line's due date (rule 2). Production orders are not an input, and nothing here
/// creates a request: the answer is a warning only.
/// <para>
/// Matching is earliest due first (a line without a due date last, then document number, line number, line id): free stock
/// first, because it is on hand now, then purchases by expected date (undated last, then order id, line id). Totals do
/// not depend on the order: the product's shortage is always <c>max(0, net demand − free stock − open purchases)</c>; the
/// order only decides which line carries it. Pure: no clock, no I/O, no rounding (kilograms stay exact).
/// </para>
/// </summary>
public static class ShortageWarningCalculator
{
    public static ShortageProductResult Calculate(ShortageProductInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Demand);
        ArgumentNullException.ThrowIfNull(input.Supply);
        RequireNonNegative(input.FreeStockKg, nameof(input.FreeStockKg));
        foreach (var line in input.Demand)
        {
            ArgumentNullException.ThrowIfNull(line);
            RequireNonNegative(line.OpenKg, nameof(line.OpenKg));
            RequireNonNegative(line.ReservedKg, nameof(line.ReservedKg));
        }

        foreach (var supply in input.Supply)
        {
            ArgumentNullException.ThrowIfNull(supply);
            RequireNonNegative(supply.RemainingKg, nameof(supply.RemainingKg));
        }

        var demand = input.Demand
            .OrderBy(line => line.DueDate.HasValue ? 0 : 1)
            .ThenBy(line => line.DueDate)
            .ThenBy(line => line.DocumentNo, StringComparer.Ordinal)
            .ThenBy(line => line.LineNo)
            .ThenBy(line => line.LineId)
            .ToArray();
        var supplies = input.Supply
            .Where(supply => supply.RemainingKg > 0m)
            .OrderBy(supply => supply.ExpectedDate.HasValue ? 0 : 1)
            .ThenBy(supply => supply.ExpectedDate)
            .ThenBy(supply => supply.OrderId)
            .ThenBy(supply => supply.LineId)
            .ToArray();
        var supplyLeft = supplies.Select(supply => supply.RemainingKg).ToArray();
        var supplyIndex = 0;
        var stockLeft = input.FreeStockKg;

        var results = new List<ShortageLineResult>(demand.Length);
        foreach (var line in demand)
        {
            var reserved = Math.Min(line.OpenKg, line.ReservedKg);
            var net = line.OpenKg - reserved;
            var need = net;

            var fromStock = Math.Min(need, stockLeft);
            stockLeft -= fromStock;
            need -= fromStock;

            var fromSupply = 0m;
            var undatedUsed = false;
            DateOnly? latest = null;
            while (need > 0m && supplyIndex < supplies.Length)
            {
                var take = Math.Min(need, supplyLeft[supplyIndex]);
                need -= take;
                fromSupply += take;
                supplyLeft[supplyIndex] -= take;
                if (supplies[supplyIndex].ExpectedDate is { } date)
                {
                    latest = latest is { } known && known >= date ? known : date;
                }
                else
                {
                    undatedUsed = true;
                }

                if (supplyLeft[supplyIndex] == 0m)
                {
                    supplyIndex++;
                }
            }

            results.Add(new ShortageLineResult(line, net, fromStock, fromSupply, need,
                Coverage(line.DueDate, need, fromSupply, undatedUsed, latest), latest));
        }

        var openDemand = demand.Sum(line => line.OpenKg);
        var netDemand = results.Sum(result => result.NetKg);
        var openSupply = supplies.Sum(supply => supply.RemainingKg);
        var shortage = results.Sum(result => result.UncoveredKg);
        var status = results.Count == 0
            ? ShortageProductStatus.Covered
            : results.Max(result => Status(result.Coverage));
        return new ShortageProductResult(
            input.ProductId,
            status,
            openDemand,
            openDemand - netDemand,
            netDemand,
            input.FreeStockKg,
            openSupply,
            shortage,
            demand.Where(line => line.DueDate.HasValue).Select(line => line.DueDate).FirstOrDefault(),
            results);
    }

    private static ShortageLineCoverage Coverage(DateOnly? dueDate, decimal uncovered, decimal fromSupply, bool undatedUsed,
        DateOnly? latestSupplyDate)
    {
        if (uncovered > 0m)
        {
            return ShortageLineCoverage.Uncovered;
        }

        if (fromSupply == 0m || dueDate is not { } due)
        {
            return ShortageLineCoverage.Covered;
        }

        if (latestSupplyDate is { } latest && latest > due)
        {
            return ShortageLineCoverage.LateCovered;
        }

        return undatedUsed ? ShortageLineCoverage.CoverageDateUnknown : ShortageLineCoverage.Covered;
    }

    private static ShortageProductStatus Status(ShortageLineCoverage coverage) => coverage switch
    {
        ShortageLineCoverage.Uncovered => ShortageProductStatus.Shortage,
        ShortageLineCoverage.LateCovered => ShortageProductStatus.LateCovered,
        ShortageLineCoverage.CoverageDateUnknown => ShortageProductStatus.CoverageDateUnknown,
        _ => ShortageProductStatus.Covered,
    };

    private static void RequireNonNegative(decimal value, string name)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(name, "Kilogram değeri negatif olamaz.");
        }
    }
}
