namespace GoymenERP.Modules.Purchasing.Domain.Orders;

/// <summary>
/// FNS-D83: the kind of rate an order records. DEC-FIN-010 admits one kind today — the TCMB indicative forex selling rate (döviz
/// satış) — so it is the default when the order names none; a second kind is a later decision, never chosen here.
/// </summary>
public enum PurchaseOrderExchangeRateKind
{
    TcmbForexSelling = 1,
}

/// <summary>
/// FNS-D79 / FNS-D91: where the recorded rate came from — the automatic SAT-D54/D170 default the order was opened with, or a number the
/// preparer typed before approval.
/// </summary>
public enum PurchaseOrderExchangeRateSource
{
    Automatic = 1,
    Manual = 2,
}

/// <summary>
/// FNS-D79 / FNS-D80 / FNS-D83 (ALC-SC33): the exchange rate written on a foreign-currency order as a NUMBER — one order-currency unit is
/// worth <see cref="Rate"/> units of <see cref="TargetCurrencyId"/> (the functional currency) — with its kind and its rate day. The rate
/// is never recomputed from a bulletin later: a later bulletin correction does not change a recorded order. Which number applies by
/// default (the previous business day's TCMB selling rate, the arife rule) is the Settings resolver's answer, never this record's.
/// <para>
/// Bounds mirror the Settings exchange-rate record (decimal(28,10): at most <see cref="MaximumScale"/> decimals and
/// <see cref="MaximumIntegerDigits"/> integer digits); a wider number is refused, never rounded.
/// </para>
/// </summary>
public sealed record PurchaseOrderExchangeRate
{
    /// <summary>The Settings exchange-rate record's decimals (SettingsExchangeRates RateMaximumScale); a technical bound, not a policy.</summary>
    public const int MaximumScale = 10;

    /// <summary>The Settings exchange-rate record's integer digits (SettingsExchangeRates RateMaximumIntegerDigits).</summary>
    public const int MaximumIntegerDigits = 18;

    public PurchaseOrderExchangeRate(decimal rate, Guid targetCurrencyId, PurchaseOrderExchangeRateKind kind, DateOnly rateDay,
        PurchaseOrderExchangeRateSource source, DateOnly? bulletinDate)
    {
        if (rate <= 0m)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderExchangeRateInvalid, "Sipariş kuru sıfırdan büyük olmalıdır.");
        }

        if (rate.Scale > MaximumScale && decimal.Round(rate, MaximumScale) != rate)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderExchangeRateInvalid,
                $"Sipariş kuru en fazla {MaximumScale} ondalık taşır; fazlası yuvarlanmaz, reddedilir.");
        }

        if (IntegerDigits(rate) > MaximumIntegerDigits)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderExchangeRateInvalid,
                $"Sipariş kurunun tam kısmı en fazla {MaximumIntegerDigits} basamaktır.");
        }

        if (!Enum.IsDefined(kind) || !Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Kur türü veya kaynağı bilinmiyor.");
        }

        // FNS-D79: an automatic rate is the resolver's answer and always names its bulletin day; a manual rate names none.
        if ((source == PurchaseOrderExchangeRateSource.Automatic) != bulletinDate.HasValue)
        {
            throw new ArgumentException("Otomatik kur bülten gününü taşır; elle girilen kur taşımaz.", nameof(bulletinDate));
        }

        Rate = rate / 1.0000000000000000000000000000m;
        TargetCurrencyId = Identity.Require(targetCurrencyId, nameof(targetCurrencyId));
        Kind = kind;
        RateDay = rateDay;
        Source = source;
        BulletinDate = bulletinDate;
    }

    /// <summary>The number written on the order (trailing zeros removed; never rounded).</summary>
    public decimal Rate { get; }

    /// <summary>The functional currency the rate converts into (TRY today, the Settings identity; never hard-coded here).</summary>
    public Guid TargetCurrencyId { get; }

    /// <summary>FNS-D83: the rate kind; <see cref="PurchaseOrderExchangeRateKind.TcmbForexSelling"/> when the order names none.</summary>
    public PurchaseOrderExchangeRateKind Kind { get; }

    /// <summary>
    /// FNS-D79 / FNS-D83: the rate day — the business day the order was created, unless the preparer names another. The bulletin that
    /// answers it is the previous business day's (SAT-D54/D170), resolved by Settings.
    /// </summary>
    public DateOnly RateDay { get; }

    public PurchaseOrderExchangeRateSource Source { get; }

    /// <summary>The bulletin day the Settings resolver answered with for an automatic rate; null for a manual rate.</summary>
    public DateOnly? BulletinDate { get; }

    private static int IntegerDigits(decimal value)
    {
        var integer = decimal.Truncate(decimal.Abs(value));
        return integer == 0m ? 0 : integer.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
    }
}

/// <summary>
/// FNS-D91: one recorded change of an order's rate — the previous rate (null for the first one), the new rate, who and when, and the
/// optional reason. The audit record carries the old and the new rate and the reason.
/// </summary>
public sealed record PurchaseOrderExchangeRateChange(
    PurchaseOrderExchangeRate? Previous,
    PurchaseOrderExchangeRate Current,
    string ChangedBy,
    DateTimeOffset ChangedAtUtc,
    string? Reason);
