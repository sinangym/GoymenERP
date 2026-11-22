namespace GoymenERP.Modules.Purchasing.Domain.Orders;

/// <summary>
/// AK-3 (SAT-D437, KIM-D47): the theoretical kilograms per piece of a piece-unit general order line, read from the Product
/// owner at the order's first approval and recorded on the document. A later card change never moves it. <see cref="SourceVersion"/>
/// is the owner's evidence fingerprint (card, formula, dimension / weight table, policy and unit profile versions) so the figure can
/// be traced to what it was computed from. Kilogram lines carry none (their quantity is already kilograms).
/// </summary>
public sealed record PurchaseOrderLineTheoreticalKilograms
{
    /// <summary>The widest scale the snapshot keeps; a finer owner figure is refused, never rounded (URN-D8 "görünen = hesaplanan").</summary>
    public const int KilogramsPerUnitScale = 6;

    /// <summary>The longest stored evidence fingerprint.</summary>
    public const int SourceVersionMaxLength = 128;

    public PurchaseOrderLineTheoreticalKilograms(decimal kilogramsPerUnit, string sourceVersion)
    {
        if (kilogramsPerUnit <= 0m || decimal.Round(kilogramsPerUnit, KilogramsPerUnitScale) != kilogramsPerUnit
            || kilogramsPerUnit >= PurchasePieceKilograms.KilogramsPerUnitBound)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.TheoreticalKilogramsInvalid,
                "Teorik kg/adet pozitif ve en fazla altı ondalıklı olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(sourceVersion) || sourceVersion != sourceVersion.Trim()
            || sourceVersion.Length > SourceVersionMaxLength || sourceVersion.Any(char.IsControl))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.TheoreticalKilogramsInvalid,
                "Teorik kg/adet kaynağının sürümü eksik veya geçersiz.");
        }

        KilogramsPerUnit = kilogramsPerUnit;
        SourceVersion = sourceVersion;
    }

    public decimal KilogramsPerUnit { get; }

    public string SourceVersion { get; }
}

/// <summary>
/// AK-3 deterministic piece/kilogram arithmetic shared by the receipt, invoice, cost and kg price reads (golden
/// <c>ADET_KG_FORMUL_GOLDEN_V1</c> TK2, ALS-D121). No tolerance, coefficient or policy is chosen here.
/// </summary>
public static class PurchasePieceKilograms
{
    /// <summary>Kilograms keep three decimals on a document line (URN-D9, BS-13).</summary>
    public const int KilogramScale = 3;

    /// <summary>The exclusive bound of a stored kilograms-per-unit figure: the decimal(18,6) column holds 12 integer digits.</summary>
    public const decimal KilogramsPerUnitBound = 1_000_000_000_000m;

    /// <summary>
    /// TK2 / SAT-D437: the theoretical kilograms of <paramref name="pieces"/> at the recorded <paramref name="kilogramsPerUnit"/>,
    /// written as a document kg value with three decimals, half away from zero (URN-D9). With whole pieces and a three-decimal
    /// figure the rounding never changes the value.
    /// </summary>
    public static decimal TheoreticalKilograms(decimal pieces, decimal kilogramsPerUnit)
    {
        if (pieces < 0m || decimal.Truncate(pieces) != pieces)
            throw new ArgumentOutOfRangeException(nameof(pieces), "Adet negatif olmayan tam sayı olmalıdır.");
        if (kilogramsPerUnit <= 0m) throw new ArgumentOutOfRangeException(nameof(kilogramsPerUnit), "Teorik kg/adet pozitif olmalıdır.");
        return decimal.Round(pieces * kilogramsPerUnit, KilogramScale, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// ALS-D121: the kilograms of a partial invoice's <paramref name="invoicedPieces"/> out of a receipt of
    /// <paramref name="acceptedPieces"/> weighed at <paramref name="measuredKilograms"/> — measured × invoiced ÷ accepted, exact.
    /// The result is the exact share; a display rounds it once (golden A09: 1250.4 × 40 / 100 = 500.160).
    /// </summary>
    public static decimal InvoicedKilogramShare(decimal measuredKilograms, decimal invoicedPieces, decimal acceptedPieces)
    {
        if (measuredKilograms <= 0m) throw new ArgumentOutOfRangeException(nameof(measuredKilograms), "Tartılan kg pozitif olmalıdır.");
        if (acceptedPieces <= 0m || decimal.Truncate(acceptedPieces) != acceptedPieces)
            throw new ArgumentOutOfRangeException(nameof(acceptedPieces), "Kabul adedi pozitif tam sayı olmalıdır.");
        if (invoicedPieces < 0m || decimal.Truncate(invoicedPieces) != invoicedPieces || invoicedPieces > acceptedPieces)
            throw new ArgumentOutOfRangeException(nameof(invoicedPieces), "Faturalanan adet kabul adedini aşamaz.");
        return measuredKilograms * invoicedPieces / acceptedPieces;
    }
}
