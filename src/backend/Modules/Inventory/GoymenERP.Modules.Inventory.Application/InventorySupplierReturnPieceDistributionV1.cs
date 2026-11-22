namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// SAT-D442 (Sinan, 2026-11-21, <c>docs/decisions/SINAN_KARARLARI_2026-11-21_TREN_KAPANIS.md</c>; 37. cevap): the returned
/// pieces of a piece-tracking label whose lineage holds more than one acceptance are distributed over those acceptances in
/// proportion to their exact lineage share, each rounded DOWN to a whole piece; the remaining pieces all go to the largest share,
/// and among equal largest shares to the first in acceptance order. The kilogram distribution stays proportional separately
/// (SAT-D433/SAT-D440).
/// <para>
/// The one live calculator is <c>SupplierReturnLineageCalculator.DistributeByShare</c> (exact shares, golden
/// ADET_KG_FORMUL_GOLDEN_V1 rows I10 and I15). K3-B P3-10: the earlier unused kilogram-weighted copy of the rule is removed so
/// the staged distribution has a single source. Behind <see cref="GoldenAccepted"/> the atomic multi-receipt supplier return
/// (AK-8b) distributes its pieces with it; with the gate closed that return keeps the 503 boundary, exactly like the SAT-D426
/// <c>ForeignSaleMarginGoldenAccepted</c> gate.
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
}
