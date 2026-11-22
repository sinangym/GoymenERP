using System.Numerics;
using GoymenERP.Modules.Inventory.Domain;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>One goods acceptance in a label's lineage: its acceptance order and its lineage kilograms (its share's weight).</summary>
public sealed record InventorySupplierReturnPieceShareV1(Guid AcceptanceId, int AcceptanceOrder, decimal LineageKg);

/// <summary>The pieces one acceptance carries out of a returned piece-tracking label.</summary>
public sealed record InventorySupplierReturnPieceAllocationV1(Guid AcceptanceId, int Pieces);

/// <summary>
/// SAT-D442 (Sinan, 2026-11-21, <c>docs/decisions/SINAN_KARARLARI_2026-11-21_TREN_KAPANIS.md</c>; 37. cevap): the returned
/// pieces of a piece-tracking label whose lineage holds more than one acceptance are distributed over those acceptances in
/// proportion to their lineage share, each rounded DOWN to a whole piece; the remaining pieces all go to the largest share,
/// and among equal largest shares to the first in acceptance order. The kilogram distribution stays proportional
/// separately (SAT-D433/SAT-D440).
/// <para>
/// The arithmetic is exact (integer floor of <c>pieces × share</c>; no decimal rounding). The rule is a stock formula whose
/// independent golden is ADET_KG_FORMUL_GOLDEN_V1 rows I10 and I15. Behind <see cref="GoldenAccepted"/> the atomic
/// multi-receipt supplier return (AK-8b) distributes its pieces with it; with the gate closed that return keeps the 503
/// boundary, exactly like the SAT-D426 <c>ForeignSaleMarginGoldenAccepted</c> gate.
/// </para>
/// </summary>
public static class InventorySupplierReturnPieceDistributionV1
{
    /// <summary>
    /// SAT-D442 golden gate. The independent golden (rows I10 and I15) is part of this train, so the gate is open; false restores
    /// the earlier boundary: a piece return over several receipts is refused 503 and nothing is staged.
    /// </summary>
    public const bool GoldenAccepted = true;

    /// <summary>The gate read through one seam so tests can pin both the open and the closed behaviour.</summary>
    public static bool IsAccepted(bool? overrideForTests = null) => overrideForTests ?? GoldenAccepted;

    public static IReadOnlyList<InventorySupplierReturnPieceAllocationV1> Distribute(
        int returnedPieces,
        IReadOnlyList<InventorySupplierReturnPieceShareV1> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);
        if (returnedPieces < 0 || shares.Count == 0 || shares.Any(share => share is null || share.LineageKg <= 0m
                || share.AcceptanceId == Guid.Empty)
            || shares.Select(share => share.AcceptanceId).Distinct().Count() != shares.Count
            || shares.Select(share => share.AcceptanceOrder).Distinct().Count() != shares.Count)
        {
            throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount,
                "Adet dağıtımı negatif olmayan adet ve benzersiz, pozitif kabul payları ister.");
        }

        var scale = shares.Max(share => share.LineageKg.Scale);
        var factor = BigInteger.Pow(10, scale);
        var weights = shares.Select(share => ToInteger(share.LineageKg, factor)).ToArray();
        var total = weights.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight);
        var pieces = weights.Select(weight => (int)BigInteger.Divide(returnedPieces * weight, total)).ToArray();
        var remainder = returnedPieces - pieces.Sum();
        var largest = Enumerable.Range(0, shares.Count)
            .OrderByDescending(index => weights[index])
            .ThenBy(index => shares[index].AcceptanceOrder)
            .First();
        pieces[largest] += remainder;
        return [.. shares.Select((share, index) => new InventorySupplierReturnPieceAllocationV1(share.AcceptanceId, pieces[index]))];
    }

    private static BigInteger ToInteger(decimal value, BigInteger factor)
    {
        var scaled = value * (decimal)factor;
        return decimal.Truncate(scaled) == scaled ? new BigInteger(scaled)
            : throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount, "Kabul payı tam ölçeklenemedi.");
    }
}
