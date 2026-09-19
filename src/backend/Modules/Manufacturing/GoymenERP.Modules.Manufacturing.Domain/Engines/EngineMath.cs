namespace GoymenERP.Modules.Manufacturing.Domain.Engines;

/// <summary>
/// Decimal-only math for the engines, so a result is the same on every platform and never passes
/// through a binary floating-point value.
/// </summary>
public static class EngineMath
{
    /// <summary>π to the 28 significant digits a decimal holds.</summary>
    public const decimal Pi = 3.1415926535897932384626433833m;

    /// <summary>
    /// The square root of a non-negative decimal by Newton's method, iterated until it no longer
    /// changes (a fixed point at the decimal's precision). The start value is a decimal estimate from
    /// the number's magnitude, not a floating-point root.
    /// </summary>
    public static decimal Sqrt(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Negatif sayının karekökü alınmaz.");
        }

        if (value == 0m)
        {
            return 0m;
        }

        var estimate = value < 1m ? 1m : value / 2m;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var next = (estimate + value / estimate) / 2m;
            if (next == estimate)
            {
                return next;
            }

            // Newton's method can settle into a two-value cycle at the last digit; either value is the root there.
            var after = (next + value / next) / 2m;
            if (after == estimate)
            {
                return Math.Min(next, estimate);
            }

            estimate = next;
        }

        throw new InvalidOperationException("Karekök hesaplaması yakınsamadı.");
    }
}
