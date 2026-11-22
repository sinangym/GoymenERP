namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>
/// ALS-D121 (golden <c>ADET_KG_FORMUL_GOLDEN_V1</c> §2 step 4, rows A09 and A13): the physical kilograms of a receipt that fall to an
/// invoiced quantity — receipt kg × invoiced quantity ÷ receipt quantity, exact. Both quantities are in the purchase unit (pieces
/// on a piece line, kilograms on a kilogram line). No rounding: the average applies its own precision once, at the end (ALS-D120).
/// </summary>
public static class PurchaseInvoiceKilogramShare
{
    /// <exception cref="ArgumentOutOfRangeException">The receipt kg or quantity is not positive, or the invoiced quantity is negative.</exception>
    public static ExactCostRatio Of(ExactCostRatio receiptKg, decimal invoicedQuantity, ExactCostRatio receiptQuantity)
    {
        ArgumentNullException.ThrowIfNull(receiptKg);
        ArgumentNullException.ThrowIfNull(receiptQuantity);
        if (receiptKg.Sign <= 0) throw new ArgumentOutOfRangeException(nameof(receiptKg), "Kabul kg pozitif olmalıdır.");
        if (receiptQuantity.Sign <= 0) throw new ArgumentOutOfRangeException(nameof(receiptQuantity), "Kabul miktarı pozitif olmalıdır.");
        if (invoicedQuantity < 0m) throw new ArgumentOutOfRangeException(nameof(invoicedQuantity), "Faturalanan miktar negatif olamaz.");
        return receiptKg.Multiply(invoicedQuantity).Divide(receiptQuantity);
    }

    /// <summary>
    /// True when a receipt's remaining kilograms and remaining quantity (after its recorded returns and reversals) still stand in
    /// the receipt's ORIGINAL kg-per-unit proportion. Only then does "remaining kg × invoiced ÷ remaining quantity" equal ALS-D121's
    /// "weighed kg × invoiced pieces ÷ accepted pieces"; otherwise no rule is chosen here.
    /// </summary>
    public static bool KeepsOriginalProportion(ExactCostRatio originalKg, decimal originalQuantity, ExactCostRatio remainingKg,
        ExactCostRatio remainingQuantity)
    {
        ArgumentNullException.ThrowIfNull(originalKg);
        ArgumentNullException.ThrowIfNull(remainingKg);
        ArgumentNullException.ThrowIfNull(remainingQuantity);
        if (originalQuantity <= 0m || remainingQuantity.Sign <= 0) return false;
        return remainingKg.Divide(remainingQuantity).CompareTo(originalKg.Divide(originalQuantity)) == 0;
    }
}
