using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

/// <summary>
/// A2a-1 (TEK-01, plan API-20): the order's currency, as the Settings money profile it was verified against when the order was
/// opened — the currency identity, the profile and its version, and the profile's scale. One currency per order (header); every
/// priced line is in it. The scale is the owner's (Ö2: the final amount's decimals); it is never chosen here. A snapshot: a later
/// profile version never changes a recorded order (the Sales draft's money snapshot, SAT-D18).
/// <para>
/// A2a-1c (ALS-D72): the profile's unit-price scale for <c>purchasing.order.v1</c> (Settings <c>MoneyProfileV1.UnitPriceScale</c>,
/// 4 for TRY) is part of the snapshot. Null — a profile that publishes none, or an order recorded before A2a-1c — keeps the A2a-1
/// rule: a unit price carries at most the amount scale. The line amount is always rounded once to <see cref="Scale"/>.
/// </para>
/// </summary>
public sealed record PurchaseOrderMoney
{
    /// <summary>
    /// The stored price columns' decimals (decimal(24,6); Ö2: "birim fiyat gerektiğinde 6 ondalığa kadar"). A technical cap, not a
    /// policy: a price is held to min(profile scale, this), so a profile wider than the column can never make SQL round a price.
    /// </summary>
    public const int MaximumPriceScale = 6;

    /// <summary>The stored currency code's bound (the Settings owner's code, e.g. "TRY"; a label, never an identity).</summary>
    public const int MaximumCurrencyCodeLength = 16;

    private PurchaseOrderMoney(Guid currencyId, string currencyCode, Guid profileId, long profileVersion, int scale, int? unitPriceScale)
    {
        CurrencyId = currencyId;
        CurrencyCode = currencyCode;
        ProfileId = profileId;
        ProfileVersion = profileVersion;
        Scale = scale;
        UnitPriceScale = unitPriceScale;
    }

    public Guid CurrencyId { get; }

    /// <summary>The owner's currency code as answered with the profile (shown beside the amounts; the identity is <see cref="CurrencyId"/>).</summary>
    public string CurrencyCode { get; }

    public Guid ProfileId { get; }
    public long ProfileVersion { get; }

    /// <summary>The money profile's own scale: the line amount is rounded to it (SAT-D11, half away from zero).</summary>
    public int Scale { get; }

    /// <summary>A2a-1c (ALS-D72): the owner's unit-price scale for the purchasing order operation, or null when none was answered.</summary>
    public int? UnitPriceScale { get; }

    /// <summary>
    /// The decimals a unit price or a discount amount (a per-unit amount taken from the unit price) may carry: min(the profile's
    /// unit-price scale — or, without one, its amount scale — and <see cref="MaximumPriceScale"/>).
    /// </summary>
    public int PriceScale => Math.Min(UnitPriceScale ?? Scale, MaximumPriceScale);

    public static PurchaseOrderMoney Of(Guid currencyId, string currencyCode, Guid profileId, long profileVersion, int scale,
        int? unitPriceScale = null)
    {
        Identity.Require(currencyId, nameof(currencyId));
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Length > MaximumCurrencyCodeLength
            || !string.Equals(currencyCode, currencyCode.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Para birimi kodu boş, kenar boşluklu veya çok uzun olamaz.", nameof(currencyCode));
        }

        Identity.Require(profileId, nameof(profileId));
        if (profileVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(profileVersion), "Para profili sürümü 1'den küçük olamaz.");
        }

        if (scale is < 0 or > Money.MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), $"Para ölçeği 0 ile {Money.MaximumScale} arasında olmalıdır.");
        }

        if (unitPriceScale is < 0 or > Money.MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPriceScale), $"Birim fiyat ölçeği 0 ile {Money.MaximumScale} arasında olmalıdır.");
        }

        return new PurchaseOrderMoney(currencyId, currencyCode, profileId, profileVersion, scale, unitPriceScale);
    }
}

/// <summary>
/// A2a-1 (ALS-D5 "elle, iskontolu"; ALS-D51/D60): a line's manual price — the unit price, the price unit it applies to and an
/// optional discount (a percent of the unit price OR an amount, never both). A line without one is "fiyat bekliyor" (ALS-D18):
/// it is approved and received as usual and has no amount. The price unit defaults to the line's entry unit; it may only be that
/// unit until a published conversion exists (ALS-D58), so the line amount is always quantity × net in one unit.
/// </summary>
/// <para>
/// A2a-2 (ALS-D38, ALS-D75 "Satış'la aynı", ALS-D76): a price is either a cash price with a maturity surcharge (vade farkı) — a
/// percent of the DISCOUNTED unit price or an amount per unit, never both, never on a 0 price — or a single deferred price typed as
/// the unit price (a plain price, no flag, ALS-D76). Only the first form carries <see cref="DeferralPercent"/> /
/// <see cref="DeferralAmount"/>.
/// </para>
public sealed record PurchaseOrderLinePrice(decimal UnitPrice, Guid PriceUnitId, decimal? DiscountPercent = null, decimal? DiscountAmount = null,
    decimal? DeferralPercent = null, decimal? DeferralAmount = null)
{
    /// <summary>The decimals of a discount percent (the column's, decimal(9,6)); a percent is a ratio, not money.</summary>
    public const int PercentScale = 6;

    /// <summary>The integer digits of the price columns (decimal(24,6)); a wider value is refused, never a provider error at the save.</summary>
    public const int MaximumIntegerDigits = 18;

    /// <summary>A2a-2 (ALS-D38): whether the line is a cash price with a maturity surcharge (vade farkı).</summary>
    public bool HasDeferral => DeferralPercent is not null || DeferralAmount is not null;

    /// <summary>
    /// The exact net unit price ((unit price − discount) + maturity surcharge), never rounded (SAT-D76); the shared calculator's.
    /// </summary>
    public decimal NetUnitPrice =>
        CommercialLinePriceCalculator.NetUnitPrice(UnitPrice, DiscountPercent, DiscountAmount, DeferralPercent, DeferralAmount);

    /// <summary>The line amount for <paramref name="quantity"/> in the price unit, rounded once to the order's money scale (SAT-D11).</summary>
    public decimal LineAmount(decimal quantity, PurchaseOrderMoney money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return CommercialLinePriceCalculator.LineAmount(quantity, UnitPrice, DiscountPercent, DiscountAmount, DeferralPercent, DeferralAmount,
            money.Scale);
    }
}
