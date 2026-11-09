using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// A2a-1 (TEK-01, plan API-20, ALS-D5, PRS-02): the order line's manual price.
/// <list type="bullet">
/// <item><b>Currency</b>: one per order, the Settings money profile of <see cref="PurchasingMoneyOperationsV1.OrderV1"/> for the
/// order's business date, read through the existing <see cref="ISettingsPrerequisiteProvider"/> (ALS-D58: no new Settings port) and
/// recorded as a snapshot. Until the Settings owner approves a profile for that operation the owner answers
/// <c>settings_provider_unavailable</c> and a priced order is refused (503, fail-closed); an order without prices needs no currency
/// (ALS-D18).</item>
/// <item><b>Price</b>: exact decimal text, parsed without rounding; every rule (not negative, decimals, unit, discount) is the domain's
/// (<see cref="PurchaseOrder"/>), answered as a 422 on <c>lines[i].price</c>. Writing a price is measured with the create name
/// (ALS-D60 yorum): no separate price-write name.</item>
/// <item><b>Disclosure</b> (PRS-02): every order detail — a read or a command's answer — carries the prices only for a caller who
/// holds <c>purchasing.orders.price.read</c>, measured before anything is written (never after the commit, S03); otherwise
/// <c>Withheld</c>.</item>
/// </list>
/// </summary>
public sealed partial class PurchasingService
{
    /// <summary>A2a-1: the currencies whose money profile answers the purchasing order operation, under the create name.</summary>
    public async Task<PurchasingCurrencyCandidatesResponseV1> GetCurrencyCandidatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.CreatePermission, cancellationToken);
        var reader = currencyCandidates ?? throw new PurchasingServiceException(503, "settings_provider_unavailable");
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = await GuardedAsync(scope, bindings.CreatePermission,
                () => reader.GetMoneyCurrencyCandidatesAsync(PurchasingMoneyOperationsV1.OrderV1, cancellationToken), cancellationToken);
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(candidates);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw exception.Code == "settings_access_denied"
                ? new PurchasingServiceException(403, "forbidden")
                : new PurchasingServiceException(503, "settings_provider_unavailable");
        }

        return new PurchasingCurrencyCandidatesResponseV1(
            [.. candidates.Select(candidate => new PurchasingCurrencyCandidateViewV1(candidate.CurrencyId, candidate.Code, candidate.DisplayLabel))]);
    }

    /// <summary>
    /// The order's money snapshot for <paramref name="currencyId"/> on <paramref name="effectiveOn"/>, or null without a currency.
    /// The profile must be this company's, name the currency and the operation, be internally valid and active on the date;
    /// otherwise <c>unsupported_currency</c> (422) — or 403/503 for the owner's denied/unavailable answers (the Sales mapping).
    /// </summary>
    private async Task<PurchaseOrderMoney?> ResolveOrderMoneyAsync(Scope scope, Guid? currencyId, DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        if (currencyId is not { } currency)
        {
            return null;
        }

        var provider = moneyProfiles ?? throw new PurchasingServiceException(503, "settings_provider_unavailable");
        MoneyProfileV1 profile;
        try
        {
            profile = await GuardedAsync(scope, bindings.CreatePermission,
                () => provider.GetMoneyProfileAsync(currency, PurchasingMoneyOperationsV1.OrderV1, effectiveOn, cancellationToken), cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw exception.Code switch
            {
                "settings_access_denied" => new PurchasingServiceException(403, "forbidden"),
                SettingsPrerequisiteCodesV1.MoneyProfileNotFound => new PurchasingServiceException(422, PurchasingProblemCodesV1.UnsupportedCurrency,
                    [new("currencyId", PurchasingProblemCodesV1.UnsupportedCurrency)]),
                "settings_profile_invalid" => new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid),
                _ => new PurchasingServiceException(503, "settings_provider_unavailable"),
            };
        }

        if (profile is null || profile.CompanyId != scope.CompanyId || profile.CurrencyId != currency || profile.Profile is null
            || profile.Profile.ProfileId == Guid.Empty || profile.Profile.Version < 1 || profile.Scale is < 0 or > Money.MaximumScale
            || profile.UnitPriceScale is < 0 or > Money.MaximumScale
            || string.IsNullOrWhiteSpace(profile.CurrencyCode) || profile.CurrencyCode.Length > PurchaseOrderMoney.MaximumCurrencyCodeLength
            || !string.Equals(profile.Operation, PurchasingMoneyOperationsV1.OrderV1, StringComparison.Ordinal))
        {
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.ReferenceProviderInvalid);
        }

        if (!profile.IsActive || effectiveOn < profile.EffectiveFrom || (profile.EffectiveThrough is { } through && effectiveOn > through))
        {
            throw new PurchasingServiceException(422, PurchasingProblemCodesV1.UnsupportedCurrency,
                [new("currencyId", PurchasingProblemCodesV1.UnsupportedCurrency)]);
        }

        // A2a-1c (ALS-D72): the owner's unit-price scale is snapshotted with the order; null keeps the amount scale for the unit price.
        return PurchaseOrderMoney.Of(currency, profile.CurrencyCode, profile.Profile.ProfileId, profile.Profile.Version, profile.Scale,
            profile.UnitPriceScale);
    }

    /// <summary>
    /// The line's price as the domain's value: exact texts parsed without rounding (a fraction wider than the stored columns is
    /// refused here with the domain's code, before any conversion could round it), the price unit defaulting to the line's unit.
    /// </summary>
    private static PurchaseOrderLinePrice? PriceOf(PurchaseOrderLineRequest line, int index)
    {
        if (line.Price is not { } price)
        {
            return null;
        }

        var field = $"lines[{index}].price";
        return new PurchaseOrderLinePrice(
            ReadPriceText(price.UnitPrice, PurchaseOrderMoney.MaximumPriceScale, PurchasingRuleCodes.PriceScaleExceeded, field),
            price.PriceUnitId ?? line.UnitId,
            price.DiscountPercent is { } percent
                ? ReadPriceText(percent, PurchaseOrderLinePrice.PercentScale, PurchasingRuleCodes.DiscountPercentOutOfRange, field)
                : null,
            price.DiscountAmount is { } amount
                ? ReadPriceText(amount, PurchaseOrderMoney.MaximumPriceScale, PurchasingRuleCodes.PriceScaleExceeded, field)
                : null,
            price.DeferralPercent is { } deferralPercent
                ? ReadPriceText(deferralPercent, PurchaseOrderLinePrice.PercentScale, PurchasingRuleCodes.DeferralPercentOutOfRange, field)
                : null,
            price.DeferralAmount is { } deferralAmount
                ? ReadPriceText(deferralAmount, PurchaseOrderMoney.MaximumPriceScale, PurchasingRuleCodes.PriceScaleExceeded, field)
                : null);
    }

    /// <summary>
    /// Ö2 "never rounded": the text (shape-checked at the wire: digits with at most one inner '.') is measured lexically FIRST — at
    /// most <paramref name="maximumScale"/> significant decimals (trailing zeros are not precision) and at most the columns' integer
    /// digits — and only then converted, so the conversion is always exact.
    /// </summary>
    private static decimal ReadPriceText(string text, int maximumScale, string scaleCode, string field)
    {
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integerPart = (point < 0 ? text : text[..point]).TrimStart('0');
        var fraction = point < 0 ? string.Empty : text[(point + 1)..].TrimEnd('0');
        if (fraction.Length > maximumScale)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new(field, scaleCode)]);
        }

        if (integerPart.Length > PurchaseOrderLinePrice.MaximumIntegerDigits)
        {
            throw new PurchasingServiceException(422, "validation_failed", [new(field, PurchasingRuleCodes.PriceOutOfRange)]);
        }

        var canonical = (integerPart.Length == 0 ? "0" : integerPart) + (fraction.Length == 0 ? string.Empty : "." + fraction);
        return decimal.Parse(canonical, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>PRS-02: Visible only for a fresh, company-wide grant of the price-read name to this very actor; never a throw for "no".</summary>
    private async Task<string> DiscloseAsync(Scope scope, CancellationToken cancellationToken)
    {
        var permission = bindings.PriceReadPermission;
        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
        {
            throw new PurchasingServiceException(503, PurchasingProblemCodesV1.PermissionUnregistered);
        }

        var read = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
        return read.IsGranted && read.IsCompanyWide && Matches(scope.CompanyId, scope.ActorId)
            ? PurchasingPriceDisclosureV1.Visible
            : PurchasingPriceDisclosureV1.Withheld;
    }

    private static PurchaseOrderLinePriceView? PriceView(PurchaseOrderLine line, PurchaseOrderMoney? money)
    {
        if (line.Price is not { } price)
        {
            return null;
        }

        var currency = money ?? throw new InvalidOperationException("Fiyatlı satırın siparişinde para birimi yok.");
        return new PurchaseOrderLinePriceView(
            CommercialLinePriceCalculator.Format(price.UnitPrice),
            price.PriceUnitId,
            price.DiscountPercent is { } percent ? CommercialLinePriceCalculator.Format(percent) : null,
            price.DiscountAmount is { } amount ? CommercialLinePriceCalculator.Format(amount) : null,
            CommercialLinePriceCalculator.Format(price.NetUnitPrice),
            CommercialLinePriceCalculator.Format(price.LineAmount(line.Quantity.Value, currency)))
        {
            DeferralPercent = price.DeferralPercent is { } deferralPercent ? CommercialLinePriceCalculator.Format(deferralPercent) : null,
            DeferralAmount = price.DeferralAmount is { } deferralAmount ? CommercialLinePriceCalculator.Format(deferralAmount) : null,
        };
    }

    private static PurchaseOrderCurrencyView? CurrencyView(PurchaseOrderMoney? money) =>
        money is null ? null : new PurchaseOrderCurrencyView(money.CurrencyId, money.CurrencyCode, money.Scale);
}
