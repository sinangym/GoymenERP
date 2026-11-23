using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Api.InventoryRuntime;

/// <summary>
/// AK-3 (ALS-D119, ALS-D120; golden <c>ADET_KG_FORMUL_GOLDEN_V1</c> §2): the cost per kilogram of a piece-line purchase receipt is
/// the paid amount ÷ the kilograms weighed at receipt. The amount is the accepted pieces × the order line's net unit cost (TA1:
/// the amount source of SAT-D295 is unchanged, only the denominator moves from the card's theoretical kg to the weighed kg). The
/// quotient stays an exact rational (ALS-D120): no intermediate rounding; the existing average applies its 4-decimal precision
/// once, at the end. A missing or zero weighed kg, a zero amount or a fractional piece count give no cost (TA3, SAT-D297) —
/// never a theoretical-kg fallback.
/// </summary>
public static class PurchaseReceiptWeighedCost
{
    /// <summary>
    /// ALS-D119's switch. The independent golden (<c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c> §2, rows A01–A14) travels with this
    /// train (SAT-D265 (5), SAT-D431), so a piece-line receipt is costed from its weighed kilograms. SAT-D298's theoretical-kg
    /// denominator is no longer used for a piece line: a receipt without weighed kilograms has no cost (TA3), never a fallback.
    /// </summary>
    public static bool PurchaseCostGoldenAccepted => true;

    /// <summary>The exact cost per weighed kilogram, or null when it cannot be computed (TA3, SAT-D297).</summary>
    public static ExactCostRatio? PerWeighedKilogram(decimal unitCost, decimal acceptedPieces, decimal? weighedKilograms)
    {
        if (unitCost <= 0m || acceptedPieces <= 0m || decimal.Truncate(acceptedPieces) != acceptedPieces
            || weighedKilograms is not > 0m)
            return null;
        return ExactCostRatio.Of(unitCost).Multiply(acceptedPieces).Divide(weighedKilograms.Value);
    }
}
