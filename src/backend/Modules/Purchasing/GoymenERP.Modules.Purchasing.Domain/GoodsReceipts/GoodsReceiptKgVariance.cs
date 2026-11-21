namespace GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;

/// <summary>
/// ALC-S16 (F-027, KLN-D45): how a bulk receipt's total kg compares with the order line's expected kg.
/// </summary>
public enum GoodsReceiptKgVarianceOutcome
{
    /// <summary>The total equals the expected kg exactly.</summary>
    Exact = 1,

    /// <summary>The difference is inside the Ayarlar tolerance: a warning, then the receipt is accepted.</summary>
    WithinTolerance = 2,

    /// <summary>The difference is outside the Ayarlar tolerance: the receipt waits for an approver.</summary>
    OutsideTolerance = 3,

    /// <summary>No tolerance is set in Ayarlar: every difference waits for an approver (KLN-D45).</summary>
    ToleranceNotSet = 4,
}

/// <summary>
/// ALC-S16 (KLN-D45): the deterministic kg variance rule. The tolerance percent is never chosen here; it is read from
/// Ayarlar by the caller and passed in (null = not set). The comparison is exact decimal arithmetic without division:
/// |received − expected| × 100 ≤ tolerance × expected.
/// </summary>
public static class GoodsReceiptKgVariance
{
    /// <summary>The largest tolerance percent the rule accepts; a wider value is a misconfiguration, never a silent clamp.</summary>
    public const decimal MaximumTolerancePercent = 100m;

    private const decimal PercentBase = 100m;

    /// <summary>The decimals a tolerance percent may carry (the stored snapshot's scale); a finer value is refused, never rounded.</summary>
    public const int TolerancePercentScale = 6;

    /// <summary>True when the Ayarlar value is a usable tolerance percent (0..100, at most <see cref="TolerancePercentScale"/> decimals).</summary>
    public static bool IsValidTolerancePercent(decimal tolerancePercent) =>
        tolerancePercent >= 0m && tolerancePercent <= MaximumTolerancePercent
        && decimal.Round(tolerancePercent, TolerancePercentScale) == tolerancePercent;

    /// <summary>
    /// Evaluates the total received kg against the expected kg. <paramref name="tolerancePercent"/> null means "not set in Ayarlar".
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The expected kg is not positive, the received kg is negative or the tolerance is outside 0..100.</exception>
    public static GoodsReceiptKgVarianceOutcome Evaluate(decimal expectedKg, decimal receivedKg, decimal? tolerancePercent)
    {
        if (expectedKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedKg), "Beklenen kg sıfırdan büyük olmalıdır.");
        }

        if (receivedKg < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedKg), "Teslim alınan kg negatif olamaz.");
        }

        if (tolerancePercent is { } invalid && !IsValidTolerancePercent(invalid))
        {
            throw new ArgumentOutOfRangeException(nameof(tolerancePercent), "Tolerans yüzdesi 0 ile 100 arasında olmalıdır.");
        }

        var difference = Math.Abs(receivedKg - expectedKg);
        if (difference == 0m)
        {
            return GoodsReceiptKgVarianceOutcome.Exact;
        }

        if (tolerancePercent is not { } tolerance)
        {
            return GoodsReceiptKgVarianceOutcome.ToleranceNotSet;
        }

        return difference * PercentBase <= tolerance * expectedKg
            ? GoodsReceiptKgVarianceOutcome.WithinTolerance
            : GoodsReceiptKgVarianceOutcome.OutsideTolerance;
    }

    /// <summary>True when the outcome is posted without an approver (exact, or inside the tolerance after the warning).</summary>
    public static bool PostsWithoutApproval(GoodsReceiptKgVarianceOutcome outcome) =>
        outcome is GoodsReceiptKgVarianceOutcome.Exact or GoodsReceiptKgVarianceOutcome.WithinTolerance;
}
