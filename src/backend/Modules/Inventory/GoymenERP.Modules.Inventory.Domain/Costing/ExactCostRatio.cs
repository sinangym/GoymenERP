using System.Globalization;
using System.Numerics;

namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>An exact value carried to the existing calculator's final rounding. This introduces no price, currency or
/// rounding policy: numerator/denominator preserve ordinary non-terminating unit conversions such as 1 / 3.</summary>
public sealed class ExactCostRatio
{
    private readonly BigInteger numerator;
    private readonly BigInteger denominator;
    private ExactCostRatio(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException();
        if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        this.numerator = numerator / gcd; this.denominator = denominator / gcd;
    }
    public string Numerator => numerator.ToString(CultureInfo.InvariantCulture);
    public string Denominator => denominator.ToString(CultureInfo.InvariantCulture);
    public int Sign => numerator.Sign;
    public static ExactCostRatio Of(decimal value)
    {
        var bits = decimal.GetBits(value);
        var magnitude = (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | (uint)bits[0];
        return new((bits[3] & int.MinValue) == 0 ? magnitude : -magnitude, BigInteger.Pow(10, (bits[3] >> 16) & 0xff));
    }
    public static bool TryParse(string? numerator, string? denominator, out ExactCostRatio? value)
    {
        value = null;
        if (!CanonicalInteger(numerator, signed: true) || !CanonicalInteger(denominator, signed: false)
            || denominator == "0") return false;
        var result = new ExactCostRatio(BigInteger.Parse(numerator!, CultureInfo.InvariantCulture),
            BigInteger.Parse(denominator!, CultureInfo.InvariantCulture));
        if (result.Numerator != numerator || result.Denominator != denominator) return false;
        value = result; return true;
    }
    public ExactCostRatio Add(ExactCostRatio other) => new(numerator * other.denominator + other.numerator * denominator, denominator * other.denominator);
    public ExactCostRatio Subtract(ExactCostRatio other) => new(numerator * other.denominator - other.numerator * denominator, denominator * other.denominator);
    public ExactCostRatio Multiply(decimal value) => Multiply(Of(value));
    public ExactCostRatio Multiply(ExactCostRatio other) => new(numerator * other.numerator, denominator * other.denominator);
    public ExactCostRatio Divide(decimal value) => Divide(Of(value));
    public ExactCostRatio Divide(ExactCostRatio other) => new(numerator * other.denominator, denominator * other.numerator);
    public int CompareTo(ExactCostRatio other) => (numerator * other.denominator).CompareTo(other.numerator * denominator);
    public decimal Round(int scale)
    {
        if (scale is < 0 or > 28) throw new ArgumentOutOfRangeException(nameof(scale));
        var magnitude = BigInteger.DivRem(BigInteger.Abs(numerator) * BigInteger.Pow(10, scale), denominator, out var remainder);
        if (remainder * 2 >= denominator) magnitude++;
        return Decimal(magnitude, numerator.Sign < 0, scale);
    }
    public bool TryToDecimal(out decimal value)
    {
        value = 0m;
        var remaining = denominator; var twos = 0; var fives = 0;
        while (remaining % 2 == 0) { remaining /= 2; twos++; }
        while (remaining % 5 == 0) { remaining /= 5; fives++; }
        var scale = Math.Max(twos, fives);
        if (remaining != BigInteger.One || scale > 28) return false;
        var magnitude = BigInteger.Abs(numerator) * BigInteger.Pow(2, scale - twos) * BigInteger.Pow(5, scale - fives);
        if (magnitude.GetBitLength() > 96) return false;
        value = Decimal(magnitude, numerator.Sign < 0, scale); return true;
    }
    private static decimal Decimal(BigInteger magnitude, bool negative, int scale)
    {
        while (scale > 0 && magnitude % 10 == 0) { magnitude /= 10; scale--; }
        if (magnitude.GetBitLength() > 96) throw new OverflowException("The final cost value is not representable by System.Decimal.");
        return new(unchecked((int)(uint)(magnitude & uint.MaxValue)), unchecked((int)(uint)((magnitude >> 32) & uint.MaxValue)),
            unchecked((int)(uint)((magnitude >> 64) & uint.MaxValue)), negative, (byte)scale);
    }
    private static bool CanonicalInteger(string? text, bool signed)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 1024) return false;
        var start = signed && text[0] == '-' ? 1 : 0;
        return text.Length > start && !(text.Length - start > 1 && text[start] == '0')
            && !(start == 1 && text[1..] == "0") && text.Skip(start).All(character => character is >= '0' and <= '9');
    }
}
