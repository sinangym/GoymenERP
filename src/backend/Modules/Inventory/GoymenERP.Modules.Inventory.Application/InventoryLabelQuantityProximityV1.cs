using System.Globalization;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// KLN-D99 (SINAN-CEVAP 34): the label list for a reservation is ordered by proximity to the requested kilograms and the
/// user picks a label by hand; the system never selects one. Deterministic and side-effect free so the suggestion reader
/// and any later report order labels the same way.
/// <list type="bullet">
/// <item>Closest first: the absolute difference between a label's unreserved kilograms and the requested kilograms,
/// ascending. A label below and a label above the request at the same distance are equally close.</item>
/// <item>Equal proximity falls back to the label code (ordinal). This tie-break is an implementation detail, reversible,
/// not a business rule.</item>
/// </list>
/// </summary>
public static class InventoryLabelQuantityProximityV1
{
    /// <summary>The longest requested-kilogram text accepted from a query string; anything longer is refused.</summary>
    public const int MaximumRequestedKgTextLength = 32;

    /// <summary>Orders two labels by proximity of their unreserved kilograms to <paramref name="requested"/>.</summary>
    public static int Compare(
        decimal leftUnreservedKg,
        string leftLabelCode,
        decimal rightUnreservedKg,
        string rightLabelCode,
        Kilograms requested)
    {
        ArgumentNullException.ThrowIfNull(leftLabelCode);
        ArgumentNullException.ThrowIfNull(rightLabelCode);
        var leftDistance = Math.Abs(leftUnreservedKg - requested.Value);
        var rightDistance = Math.Abs(rightUnreservedKg - requested.Value);
        var byDistance = leftDistance.CompareTo(rightDistance);
        return byDistance != 0 ? byDistance : string.CompareOrdinal(leftLabelCode, rightLabelCode);
    }

    /// <summary>
    /// Reads the requested kilograms from query text: ASCII digits with at most one '.' and at most
    /// <see cref="Kilograms.MaximumDecimals"/> decimals, strictly positive. Signs, exponents, commas, spaces and extra
    /// decimals are refused rather than rounded, so the order is never computed for a quantity the user did not type.
    /// </summary>
    public static bool TryParseRequestedKg(string? text, out Kilograms requested)
    {
        requested = Kilograms.Zero;
        if (string.IsNullOrEmpty(text) || text.Length > MaximumRequestedKgTextLength)
        {
            return false;
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var whole = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        if (whole.Length == 0 || !whole.All(char.IsAsciiDigit)
            || (dot >= 0 && (fraction.Length == 0 || fraction.Length > Kilograms.MaximumDecimals
                || !fraction.All(char.IsAsciiDigit))))
        {
            return false;
        }

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        try
        {
            requested = Kilograms.Positive(value);
            return true;
        }
        catch (InventoryRuleException)
        {
            requested = Kilograms.Zero;
            return false;
        }
    }
}
