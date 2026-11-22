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
    /// ALS-D122's switch (SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR): after a partial return of a piece receipt, the invoiced
    /// pieces take "remaining kg × invoiced pieces ÷ remaining pieces". Its golden rows are prepared by an independent session
    /// (SAT-D431) and are not in the train yet, so this stays false: a piece share whose return did not leave in the receipt's own
    /// proportion keeps waiting (<c>purchase_invoice_cost_piece_reversal_basis_unavailable</c>).
    /// </summary>
    public static bool PurchaseInvoiceRemainingShareGoldenAccepted => false;

    /// <summary>ALS-D124: a partial invoice's kilogram share keeps three decimals.</summary>
    public const int ShareScale = 3;

    /// <summary>
    /// ALS-D122 / ALS-D123 / ALS-D124 (behind <see cref="PurchaseInvoiceRemainingShareGoldenAccepted"/>): the kilograms of an invoice
    /// on a piece receipt — remaining kg × invoiced pieces ÷ remaining pieces, rounded to three decimals half away from zero. The
    /// remaining kg and pieces are the caller's CURRENT figures: the receipt's weighed kg and pieces less every return so far and
    /// less the kg and pieces of earlier invoices (ALS-D123). The invoice that takes the last remaining pieces takes the whole
    /// remaining kg, so the shares add up to the receipt's (returns deducted) kilograms exactly (ALS-D124).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Nothing remains, or the invoiced pieces are not within the remaining pieces.</exception>
    public static ExactCostRatio RemainingShare(ExactCostRatio remainingKg, ExactCostRatio remainingPieces, decimal invoicedPieces)
    {
        ArgumentNullException.ThrowIfNull(remainingKg);
        ArgumentNullException.ThrowIfNull(remainingPieces);
        if (remainingKg.Sign <= 0) throw new ArgumentOutOfRangeException(nameof(remainingKg), "Kalan kg pozitif olmalıdır.");
        if (remainingPieces.Sign <= 0) throw new ArgumentOutOfRangeException(nameof(remainingPieces), "Kalan adet pozitif olmalıdır.");
        var invoiced = ExactCostRatio.Of(invoicedPieces);
        var comparison = invoiced.CompareTo(remainingPieces);
        if (invoicedPieces <= 0m || comparison > 0)
            throw new ArgumentOutOfRangeException(nameof(invoicedPieces), "Faturalanan adet kalan adedi aşamaz.");
        return comparison == 0
            ? remainingKg
            : ExactCostRatio.Of(Of(remainingKg, invoicedPieces, remainingPieces).Round(ShareScale));
    }

    /// <summary>
    /// Whether a piece-line share's kilograms can be applied. Without a non-proportional return ALS-D121 and ALS-D122 agree, so the
    /// share is always decided; with one it is decided only while ALS-D122 is accepted (<paramref name="remainingShareAccepted"/>),
    /// and the share is then <see cref="Of"/> over the remaining kg and remaining pieces.
    /// </summary>
    public static bool PieceShareIsDecided(ExactCostRatio originalKg, decimal originalQuantity, ExactCostRatio remainingKg,
        ExactCostRatio remainingQuantity, bool remainingShareAccepted)
    {
        ArgumentNullException.ThrowIfNull(remainingKg);
        ArgumentNullException.ThrowIfNull(remainingQuantity);
        if (KeepsOriginalProportion(originalKg, originalQuantity, remainingKg, remainingQuantity)) return true;
        return remainingShareAccepted && originalQuantity > 0m && remainingKg.Sign > 0 && remainingQuantity.Sign > 0;
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
