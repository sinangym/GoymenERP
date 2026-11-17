using System.Numerics;

namespace GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

public sealed record DispatchKilogramSourceLine(int LineNo, Guid ShipmentId, Guid ProductId, decimal TheoreticalKg);
public sealed record DispatchKilogramProductTotal(Guid ProductId, decimal Kilograms);
public sealed record DispatchKilogramDeclaredLine(int LineNo, Guid ShipmentId, Guid ProductId, decimal Kilograms);
public sealed record DispatchKilogramAllocatedLine(int LineNo, Guid ShipmentId, Guid ProductId, decimal TheoreticalKg, decimal Kilograms);

/// <summary>
/// SAT-D421/D424/D425: kilogram allocation only. An issued external document is copied exactly;
/// absent one, product scale kilograms are allocated by theoretical kilograms, with the remainder
/// on the first heaviest source line. Invoice consumers carry these source shares without reallocating.
/// </summary>
public static class DispatchKilogramAllocation
{
    public const string InvalidSource = "logistics_dispatch_kg_source_invalid";
    public const string InvalidDistribution = "logistics_dispatch_kg_distribution_invalid";

    public static IReadOnlyList<DispatchKilogramAllocatedLine> Calculate(
        IReadOnlyList<DispatchKilogramSourceLine> lines, IReadOnlyList<DispatchKilogramProductTotal> totals)
    {
        RequireSources(lines, totals);
        var allocated = new Dictionary<int, decimal>();
        foreach (var group in lines.GroupBy(line => line.ProductId))
        {
            var total = totals.Single(total => total.ProductId == group.Key).Kilograms;
            var denominator = group.Sum(line => line.TheoreticalKg);
            var remainderLine = group.OrderByDescending(line => line.TheoreticalKg).ThenBy(line => line.LineNo).First();
            var sum = 0m;
            foreach (var line in group.Where(line => line.LineNo != remainderLine.LineNo))
            {
                var share = ProportionalKg(total, line.TheoreticalKg, denominator);
                allocated.Add(line.LineNo, share);
                sum = checked(sum + share);
            }
            var remainder = total - sum;
            if (remainder < 0m) throw Refused(InvalidDistribution);
            allocated.Add(remainderLine.LineNo, remainder);
        }
        return lines.OrderBy(line => line.LineNo).Select(line => new DispatchKilogramAllocatedLine(line.LineNo,
            line.ShipmentId, line.ProductId, line.TheoreticalKg, allocated[line.LineNo])).ToArray();
    }

    /// <summary>The caller must validate the real finalized document evidence; ETTN presence alone is insufficient.</summary>
    public static IReadOnlyList<DispatchKilogramAllocatedLine> CopyFinalizedDocument(
        IReadOnlyList<DispatchKilogramSourceLine> lines, IReadOnlyList<DispatchKilogramProductTotal> totals,
        IReadOnlyList<DispatchKilogramDeclaredLine> declared)
    {
        RequireSources(lines, totals);
        ArgumentNullException.ThrowIfNull(declared);
        if (declared.Count != lines.Count || declared.Any(line => line is null || !NonnegativeKg(line.Kilograms))
            || declared.Select(line => line.LineNo).Distinct().Count() != declared.Count) throw Refused(InvalidDistribution);
        foreach (var line in lines)
        {
            var fact = declared.SingleOrDefault(fact => fact.LineNo == line.LineNo);
            if (fact is null || fact.ShipmentId != line.ShipmentId || fact.ProductId != line.ProductId) throw Refused(InvalidDistribution);
        }
        foreach (var total in totals)
            if (declared.Where(line => line.ProductId == total.ProductId).Sum(line => line.Kilograms) != total.Kilograms)
                throw Refused(InvalidDistribution);
        return lines.OrderBy(line => line.LineNo).Select(line => new DispatchKilogramAllocatedLine(line.LineNo,
            line.ShipmentId, line.ProductId, line.TheoreticalKg, declared.Single(fact => fact.LineNo == line.LineNo).Kilograms)).ToArray();
    }

    private static void RequireSources(IReadOnlyList<DispatchKilogramSourceLine> lines, IReadOnlyList<DispatchKilogramProductTotal> totals)
    {
        ArgumentNullException.ThrowIfNull(lines); ArgumentNullException.ThrowIfNull(totals);
        if (lines.Count is < 1 or > WeighingDifferenceCalculator.MaximumLines || totals.Count == 0
            || lines.Any(line => line is null || line.LineNo < 1 || line.ShipmentId == Guid.Empty || line.ProductId == Guid.Empty || !PositiveKg(line.TheoreticalKg))
            || totals.Any(total => total is null || total.ProductId == Guid.Empty || !NonnegativeKg(total.Kilograms))
            || lines.Select(line => line.LineNo).Distinct().Count() != lines.Count
            || lines.Select(line => line.ShipmentId).Distinct().Count() != lines.Count
            || totals.Select(total => total.ProductId).Distinct().Count() != totals.Count
            || !lines.Select(line => line.ProductId).ToHashSet().SetEquals(totals.Select(total => total.ProductId))) throw Refused(InvalidSource);
    }

    private static bool PositiveKg(decimal value) => value > 0m && NonnegativeKg(value);
    private static bool NonnegativeKg(decimal value) => value >= 0m && value < 1_000_000_000_000_000m
        && decimal.Round(value, Quantity.KilogramScale) == value;

    // Integer thousandths preserve exact proportions and midpoint rounding even at decimal(18,3)'s upper boundary.
    private static decimal ProportionalKg(decimal total, decimal weight, decimal denominator)
    {
        const decimal factor = 1000m; // Quantity.KilogramScale = 3; storage unit is one gram.
        var numerator = new BigInteger(total * factor) * new BigInteger(weight * factor);
        var divisor = new BigInteger(denominator * factor);
        var quotient = BigInteger.DivRem(numerator, divisor, out var remainder);
        if (remainder * 2 >= divisor) quotient++;
        return (decimal)quotient / factor;
    }
    private static WeighingDifferenceRuleException Refused(string code) => new(code, "İrsaliye satır kilogram kaynağı tutarlı değil; dağılım oluşturulmadı.");
}
