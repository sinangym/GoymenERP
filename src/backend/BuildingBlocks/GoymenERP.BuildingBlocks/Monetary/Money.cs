using System.Globalization;

namespace GoymenERP.BuildingBlocks.Monetary;

/// <summary>The stable refusal codes of the shared money value objects.</summary>
public static class MoneyRuleCodes
{
    /// <summary>Ö2: an amount carries more significant decimals than its scale; refused, never rounded.</summary>
    public const string ScaleExceeded = "money_scale_exceeded";

    /// <summary>Two amounts in different currencies were added, subtracted or compared; there is no implicit conversion.</summary>
    public const string CurrencyMismatch = "money_currency_mismatch";

    /// <summary>Two amounts of one currency at different scales were added or subtracted; the caller chooses the scale.</summary>
    public const string ScaleMismatch = "money_scale_mismatch";

    /// <summary>An exchange rate was applied to an amount in a currency it does not convert from.</summary>
    public const string ExchangeRateCurrencyMismatch = "exchange_rate_currency_mismatch";
}

/// <summary>A refused money operation: the stable code from <see cref="MoneyRuleCodes"/>. Nothing was computed.</summary>
public sealed class MoneyRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// An exact amount in one currency at one scale (TEK-01). The scale is carried by the value, but its
/// value is never chosen here: it is the Settings owner's approved money profile for the currency and
/// operation (Ö2's initial profile is 2 decimals for a final amount; the unit-price scale "up to 6"
/// is not a published profile field yet, T28). A caller passes the profile's scale; no default exists.
/// <list type="bullet">
/// <item><see cref="Of"/> is exact: more significant decimals than the scale are refused
/// (<see cref="MoneyRuleCodes.ScaleExceeded"/>), never rounded; trailing zeros are not precision.</item>
/// <item><see cref="Round"/> is the one rounding in the system (SAT-D11, same as the invoice rule):
/// half away from zero (<see cref="MidpointRounding.AwayFromZero"/>) to the scale the caller passes.
/// When to round — per line, per total — is the calculator's rule, not this type's.</item>
/// <item>Arithmetic and comparison need the same currency (and, for arithmetic, the same scale);
/// there is no implicit conversion. A sign is allowed: whether a negative amount is meaningful is the
/// caller's rule.</item>
/// </list>
/// The currency is the Settings currency identity (<c>CurrencyId</c>), the key every money profile is
/// published under.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>System.Decimal's own maximum scale; a technical bound, not a business policy.</summary>
    public const int MaximumScale = 28;

    /// <summary>SAT-D11: the only rounding mode a money amount is rounded with.</summary>
    private const MidpointRounding RoundingMode = MidpointRounding.AwayFromZero;

    private Money(decimal amount, Guid currencyId, int scale)
    {
        Amount = amount;
        CurrencyId = currencyId;
        Scale = scale;
    }

    public decimal Amount { get; }
    public Guid CurrencyId { get; }

    /// <summary>The number of decimals the amount may carry, as the Settings money profile published it.</summary>
    public int Scale { get; }

    public bool IsZero => Amount == 0m;
    public bool IsNegative => Amount < 0m;

    /// <summary>The exact amount; refused with <see cref="MoneyRuleCodes.ScaleExceeded"/> when it does not fit the scale.</summary>
    public static Money Of(decimal amount, Guid currencyId, int scale)
    {
        RequireIdentity(currencyId, scale);
        if (!FitsScale(amount, scale))
        {
            throw new MoneyRuleException(MoneyRuleCodes.ScaleExceeded,
                $"Tutar en fazla {scale} ondalık taşıyabilir; fazlası yuvarlanmaz, reddedilir.");
        }

        return new Money(Normalize(amount), currencyId, scale);
    }

    /// <summary>The exact amount, or false when it does not fit the scale (the caller maps that to its own code).</summary>
    public static bool TryOf(decimal amount, Guid currencyId, int scale, out Money money)
    {
        RequireIdentity(currencyId, scale);
        if (!FitsScale(amount, scale))
        {
            money = default;
            return false;
        }

        money = new Money(Normalize(amount), currencyId, scale);
        return true;
    }

    /// <summary>SAT-D11: an exact computed amount rounded half away from zero to the profile's scale.</summary>
    public static Money Round(decimal exactAmount, Guid currencyId, int scale)
    {
        RequireIdentity(currencyId, scale);
        return new Money(Normalize(decimal.Round(exactAmount, scale, RoundingMode)), currencyId, scale);
    }

    public static Money Zero(Guid currencyId, int scale) => Of(0m, currencyId, scale);

    /// <summary>Whether the amount has at most <paramref name="scale"/> significant decimals (12.50 fits 1; trailing zeros are not precision).</summary>
    public static bool FitsScale(decimal amount, int scale)
    {
        RequireScale(scale);
        return SignificantDecimals(amount) <= scale;
    }

    /// <summary>The number of decimals that carry a non-zero digit: 12.5000 → 1, 12.5001 → 4, 1000 → 0.</summary>
    public static int SignificantDecimals(decimal amount) => Normalize(amount).Scale;

    public Money Add(Money other)
    {
        RequireSameScale(other);
        return new Money(Normalize(Amount + other.Amount), CurrencyId, Scale);
    }

    public Money Subtract(Money other)
    {
        RequireSameScale(other);
        return new Money(Normalize(Amount - other.Amount), CurrencyId, Scale);
    }

    public Money Negate() => new(-Amount, CurrencyId, Scale);

    /// <summary>Orders two amounts of one currency; the scale does not change the value (12.5 at 2 equals 12.50 at 6).</summary>
    public int CompareTo(Money other)
    {
        RequireSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    public bool IsGreaterThan(Money other) => CompareTo(other) > 0;

    public bool IsLessThan(Money other) => CompareTo(other) < 0;

    /// <summary>The amount padded to its scale with the invariant culture ("12.50" at scale 2) and the currency identity.</summary>
    public override string ToString() =>
        $"{Amount.ToString("F" + Scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)} {CurrencyId}";

    private void RequireSameCurrency(Money other)
    {
        if (other.CurrencyId != CurrencyId)
        {
            throw new MoneyRuleException(MoneyRuleCodes.CurrencyMismatch,
                "Para birimleri farklı; kur dönüşümü olmadan birlikte işlenemez.");
        }
    }

    private void RequireSameScale(Money other)
    {
        RequireSameCurrency(other);
        if (other.Scale != Scale)
        {
            throw new MoneyRuleException(MoneyRuleCodes.ScaleMismatch,
                "Tutarların ondalık ölçeği farklı; ölçek para profilinden seçilmelidir.");
        }
    }

    private static void RequireIdentity(Guid currencyId, int scale)
    {
        if (currencyId == Guid.Empty)
        {
            throw new ArgumentException("Para birimi kimliği boş olamaz.", nameof(currencyId));
        }

        RequireScale(scale);
    }

    private static void RequireScale(int scale)
    {
        if (scale is < 0 or > MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), $"Para ölçeği 0 ile {MaximumScale} arasında olmalıdır.");
        }
    }

    /// <summary>Drops trailing zeros without changing the value, so equal amounts also hash and print equally.</summary>
    private static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;
}
