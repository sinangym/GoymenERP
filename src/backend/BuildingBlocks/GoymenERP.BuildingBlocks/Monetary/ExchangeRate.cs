using System.Numerics;

namespace GoymenERP.BuildingBlocks.Monetary;

/// <summary>
/// One positive rate that converts an amount from a source currency into a target currency (TEK-01):
/// one source-currency unit is worth <see cref="Rate"/> target-currency units. The value object
/// chooses nothing: which rate applies (DEC-FIN-010: previous business day's TCMB selling rate as the
/// starting assumption for USD; the cut-off hour and holiday policy are open), its source, date and
/// decimals are the Settings/Finance owners' answer, carried in by the caller. No rate scale is
/// enforced here because none is published yet.
/// <para>
/// <see cref="ConvertExact"/> is exact or refused: the product is computed without rounding and an
/// answer System.Decimal cannot hold exactly is an <see cref="OverflowException"/>, never a silently
/// rounded amount. <see cref="Convert"/> then rounds through <see cref="Money.Round"/>, the one
/// rounding (SAT-D11), to the target scale the caller takes from the target currency's money profile.
/// </para>
/// <para>The inverse rate (target → source) is its own published rate, never 1 / <see cref="Rate"/>; this type does not derive it.</para>
/// </summary>
public readonly record struct ExchangeRate
{
    private ExchangeRate(Guid sourceCurrencyId, Guid targetCurrencyId, decimal rate)
    {
        SourceCurrencyId = sourceCurrencyId;
        TargetCurrencyId = targetCurrencyId;
        Rate = rate;
    }

    public Guid SourceCurrencyId { get; }
    public Guid TargetCurrencyId { get; }
    public decimal Rate { get; }

    public static ExchangeRate Of(Guid sourceCurrencyId, Guid targetCurrencyId, decimal rate)
    {
        if (sourceCurrencyId == Guid.Empty || targetCurrencyId == Guid.Empty)
        {
            throw new ArgumentException("Kurun kaynak ve hedef para birimi kimliği boş olamaz.");
        }

        if (sourceCurrencyId == targetCurrencyId)
        {
            throw new ArgumentException("Kur iki farklı para birimi arasında olmalıdır.", nameof(targetCurrencyId));
        }

        if (rate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Kur sıfırdan büyük olmalıdır.");
        }

        return new ExchangeRate(sourceCurrencyId, targetCurrencyId, rate / 1.0000000000000000000000000000m);
    }

    /// <summary>The exact target-currency value of <paramref name="source"/>, before any rounding.</summary>
    public decimal ConvertExact(Money source)
    {
        if (source.CurrencyId != SourceCurrencyId)
        {
            throw new MoneyRuleException(MoneyRuleCodes.ExchangeRateCurrencyMismatch,
                "Tutarın para birimi kurun kaynak para birimi değil.");
        }

        return ExactProduct(source.Amount, Rate);
    }

    /// <summary>The converted amount rounded once (SAT-D11) to <paramref name="targetScale"/>, the target profile's scale.</summary>
    public Money Convert(Money source, int targetScale) => Money.Round(ConvertExact(source), TargetCurrencyId, targetScale);

    private static decimal ExactProduct(decimal left, decimal right)
    {
        var (leftMantissa, leftScale) = Decompose(left);
        var (rightMantissa, rightScale) = Decompose(right);
        var mantissa = leftMantissa * rightMantissa;
        var scale = leftScale + rightScale;
        while (scale > 0 && !mantissa.IsZero && mantissa % 10 == 0)
        {
            mantissa /= 10;
            scale--;
        }

        if (mantissa.IsZero)
        {
            return 0m;
        }

        var magnitude = BigInteger.Abs(mantissa);
        if (scale > Money.MaximumScale || magnitude.GetBitLength() > 96)
        {
            throw new OverflowException("The exact converted amount is not representable by System.Decimal.");
        }

        var low = unchecked((int)(uint)(magnitude & uint.MaxValue));
        var middle = unchecked((int)(uint)((magnitude >> 32) & uint.MaxValue));
        var high = unchecked((int)(uint)((magnitude >> 64) & uint.MaxValue));
        return new decimal(low, middle, high, mantissa.Sign < 0, (byte)scale);
    }

    private static (BigInteger Mantissa, int Scale) Decompose(decimal value)
    {
        var bits = decimal.GetBits(value);
        var mantissa = (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | new BigInteger((uint)bits[0]);
        var negative = (bits[3] & int.MinValue) != 0;
        var scale = (bits[3] >> 16) & 0xFF;
        return (negative ? -mantissa : mantissa, scale);
    }
}
