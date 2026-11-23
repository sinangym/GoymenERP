using System.Globalization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// The deviations of a similar product: the largest and the summed relative deviation over the compared dimensions,
/// each |candidate − requested| / requested (a fraction, not a percent).
/// </summary>
public sealed record InventorySimilarityScoreV1(decimal MaximumDeviation, decimal TotalDeviation, int ComparedDimensions)
{
    /// <summary>The largest deviation as percent text with <see cref="PercentDecimals"/> decimals, rounded half away.</summary>
    public string MaximumDeviationPercentText =>
        decimal.Round(MaximumDeviation * 100m, PercentDecimals, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture);

    public const int PercentDecimals = 2;
}

/// <summary>
/// The one deterministic "benzer ürün" rule (KLN-D70), used by both the suggestion read and the substitute create:
/// <list type="bullet">
/// <item>No tolerance configured (null) — nothing is similar; only the same product is suggested (KLN-D40).</item>
/// <item>Both products must have accepted facts and the same similarity key (material, quality, product type).</item>
/// <item>KLN-D95: both products must state a coating and the coatings must be equal (ordinal); a different or missing
/// coating is never similar, whatever the measures.</item>
/// <item>Thickness, width and length are each compared with their own symmetric ± percentage. A dimension missing on
/// either side does not enter the comparison; when no dimension can be compared the product is not suggested.</item>
/// <item>The same product is never "similar" to itself here; the same-product path does not use this rule.</item>
/// </list>
/// Ordering (ASSUMPTION-STK-SIMILAR-ORDER-1): the largest relative deviation first, then the summed deviation, then the
/// label code (ordinal).
/// </summary>
public static class InventorySimilarProductCalculatorV1
{
    public static InventorySimilarityScoreV1? Evaluate(
        InventoryProductSimilarityFactsV1 requested,
        InventoryProductSimilarityFactsV1 candidate,
        InventorySimilarProductToleranceV1? tolerance)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(candidate);
        if (tolerance is null || !tolerance.IsWellFormed)
        {
            return null;
        }

        if (requested.ProductId == candidate.ProductId
            || requested.SimilarityKey is null || candidate.SimilarityKey is null
            || !string.Equals(requested.SimilarityKey, candidate.SimilarityKey, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(requested.CoatingCode) || string.IsNullOrWhiteSpace(candidate.CoatingCode)
            || !string.Equals(requested.CoatingCode, candidate.CoatingCode, StringComparison.Ordinal))
        {
            return null;
        }

        var maximum = 0m;
        var total = 0m;
        var compared = 0;
        foreach (var (wanted, offered, percent) in new[]
                 {
                     (requested.ThicknessMm, candidate.ThicknessMm, tolerance.ThicknessPercent),
                     (requested.WidthMm, candidate.WidthMm, tolerance.WidthPercent),
                     (requested.LengthMm, candidate.LengthMm, tolerance.LengthPercent),
                 })
        {
            if (wanted is not { } requestedMm || offered is not { } candidateMm)
            {
                continue;
            }

            if (requestedMm <= 0m || candidateMm <= 0m)
            {
                // A non-positive nominal measure is not an accepted fact; never compare against it.
                return null;
            }

            var deviation = Math.Abs(candidateMm - requestedMm) / requestedMm;
            if (deviation * 100m > percent)
            {
                return null;
            }

            maximum = Math.Max(maximum, deviation);
            total += deviation;
            compared++;
        }

        return compared == 0 ? null : new InventorySimilarityScoreV1(maximum, total, compared);
    }

    /// <summary>Orders similar candidates by ASSUMPTION-STK-SIMILAR-ORDER-1.</summary>
    public static int Compare(
        InventorySimilarityScoreV1 left,
        string leftLabelCode,
        InventorySimilarityScoreV1 right,
        string rightLabelCode)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var byMaximum = left.MaximumDeviation.CompareTo(right.MaximumDeviation);
        if (byMaximum != 0)
        {
            return byMaximum;
        }

        var byTotal = left.TotalDeviation.CompareTo(right.TotalDeviation);
        return byTotal != 0 ? byTotal : string.CompareOrdinal(leftLabelCode, rightLabelCode);
    }
}
