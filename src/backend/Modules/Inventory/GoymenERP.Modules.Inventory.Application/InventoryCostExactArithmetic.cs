using System.Numerics;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Representation only: a movement cost is exact before the moving-average calculator's published rounding.
/// System.Decimal arithmetic may silently round a division or product, so an unrepresentable value stays unavailable.</summary>
public static class InventoryCostExactArithmetic
{
    public static bool TryDivide(decimal numerator, decimal denominator, out decimal result)
    {
        result = 0m;
        if (denominator == 0m) return false;
        var (a, aScale) = Parts(numerator);
        var (b, bScale) = Parts(denominator);
        a *= BigInteger.Pow(10, bScale);
        b *= BigInteger.Pow(10, aScale);
        if (b.Sign < 0) { a = -a; b = -b; }
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(a), b);
        a /= gcd; b /= gcd;
        var twos = 0; var fives = 0;
        while (b % 2 == 0) { b /= 2; twos++; }
        while (b % 5 == 0) { b /= 5; fives++; }
        if (b != BigInteger.One) return false;
        var scale = Math.Max(twos, fives);
        if (scale > 28) return false;
        a *= BigInteger.Pow(2, scale - twos) * BigInteger.Pow(5, scale - fives);
        return TryCreate(a, scale, out result);
    }

    public static bool TryMultiply(decimal left, decimal right, out decimal result)
    {
        var (a, aScale) = Parts(left); var (b, bScale) = Parts(right);
        return TryCreate(a * b, aScale + bScale, out result);
    }

    private static bool TryCreate(BigInteger mantissa, int scale, out decimal result)
    {
        result = 0m;
        if (mantissa.IsZero) return true;
        while (scale > 0 && mantissa % 10 == 0) { mantissa /= 10; scale--; }
        var magnitude = BigInteger.Abs(mantissa);
        if (scale > 28 || magnitude.GetBitLength() > 96) return false;
        result = new decimal(unchecked((int)(uint)(magnitude & uint.MaxValue)),
            unchecked((int)(uint)((magnitude >> 32) & uint.MaxValue)),
            unchecked((int)(uint)((magnitude >> 64) & uint.MaxValue)), mantissa.Sign < 0, (byte)scale);
        return true;
    }

    private static (BigInteger Mantissa, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var magnitude = (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | (uint)bits[0];
        return ((bits[3] & int.MinValue) == 0 ? magnitude : -magnitude, (bits[3] >> 16) & 0xff);
    }
}
