using System.Globalization;
using System.Numerics;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>Exact display evidence (for example one purchase unit costs 1/3 per kg), never an approximate price.</summary>
public sealed record ProductionExactRatioV1(string Numerator, string Denominator)
{
    public bool IsValid(bool allowZero = false) => TryRead(out var numerator, out _) && (allowZero || numerator > 0);

    public ProductionExactRatioV1 Multiply(decimal quantity)
    {
        if (quantity < 0 || !TryRead(out var numerator, out var denominator)) throw new ArgumentException("Kesin oran geçersiz.");
        var bits = decimal.GetBits(quantity); var scale = (bits[3] >> 16) & 0xFF;
        var coefficient = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        numerator *= coefficient; denominator *= BigInteger.Pow(10, scale);
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new((numerator / gcd).ToString(CultureInfo.InvariantCulture), (denominator / gcd).ToString(CultureInfo.InvariantCulture));
    }

    private bool TryRead(out BigInteger numerator, out BigInteger denominator)
    {
        numerator = denominator = default;
        return Canonical(Numerator) && Canonical(Denominator) && BigInteger.TryParse(Numerator, NumberStyles.None, CultureInfo.InvariantCulture, out numerator)
            && BigInteger.TryParse(Denominator, NumberStyles.None, CultureInfo.InvariantCulture, out denominator) && denominator > 0
            && BigInteger.GreatestCommonDivisor(numerator, denominator) == BigInteger.One;
        static bool Canonical(string value) => !string.IsNullOrEmpty(value) && value.Length <= 160 && (value.Length == 1 || value[0] != '0') && value.All(char.IsAsciiDigit);
    }
}
