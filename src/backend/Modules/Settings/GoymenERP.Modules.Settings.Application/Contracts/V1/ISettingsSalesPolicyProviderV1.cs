namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Company-scoped, immutable Sales policy reference data (MOD-03 slice 1; SAT-D2, SAT-D5, SAT-D6 of
/// <c>docs/decisions/SINAN_KARARLARI_2026-10-25_SATIS.md</c>). The company always comes from the
/// server context and is never a request input. A result is reference metadata, not an
/// authorization: it neither approves a discount, a delivery or an order nor grants any permission.
/// A consumer persists the exact returned identity, version, values and window together and
/// revalidates them in its own command transaction.
/// <para>
/// Every read fails with a <see cref="SettingsPrerequisiteException"/>, never a null, a zero or a
/// default: <c>settings_access_denied</c> before anything about the source is revealed;
/// <c>settings_provider_unavailable</c> when the company has no approved Sales policy source;
/// <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/> when the source is reachable but has
/// no policy for the identity asked about; <see cref="SettingsPrerequisiteCodesV1.SalesPolicyRequestInvalid"/>
/// for an empty limit group or currency id (after the access check, before any lookup);
/// <c>settings_profile_invalid</c> for a malformed source.
/// </para>
/// </summary>
public interface ISettingsSalesPolicyProviderV1
{
    /// <summary>The company's over/under delivery tolerance in force on the date (SAT-D5).</summary>
    Task<SalesDeliveryTolerancePolicyV1> GetSalesDeliveryToleranceAsync(
        DateOnly effectiveOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// The maximum discount percent of one limit group (SAT-D2). <paramref name="limitGroupId"/> is an
    /// opaque identity the Identity owner's role mapping supplies; it is never a role name.
    /// <see cref="SettingsPrerequisiteCodesV1.SalesPolicyNotFound"/> means the group has no approved
    /// limit, which SAT-D2 defines as "every discount goes to approval" — never "no limit".
    /// </summary>
    Task<SalesDiscountLimitPolicyV1> GetSalesDiscountLimitAsync(
        Guid limitGroupId, DateOnly effectiveOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// The order-approval amount threshold for one currency (SAT-D6). When
    /// <see cref="SalesApprovalAmountThresholdPolicyV1.IsEnabled"/> is false the amount is null and
    /// no amount-based routing applies.
    /// </summary>
    Task<SalesApprovalAmountThresholdPolicyV1> GetSalesApprovalAmountThresholdAsync(
        Guid currencyId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
}

/// <summary>
/// Delivery tolerance percents relative to the ordered quantity, with 0 ≤ warn ≤ approval ≤ block ≤ 100.
/// <para>
/// Consumer contract (SAT-D15, <c>docs/decisions/SINAN_KARARLARI_2026-10-26_SATIS_EK.md</c>): the
/// deviation is |delivered − ordered| / ordered × 100, so over and under delivery are evaluated with
/// the same thresholds, and every bound is inclusive — a bound value falls into the band it starts:
/// <list type="bullet">
/// <item>deviation &lt; <see cref="WarnPercent"/>: within tolerance;</item>
/// <item><see cref="WarnPercent"/> ≤ deviation &lt; <see cref="ApprovalPercent"/>: warn;</item>
/// <item><see cref="ApprovalPercent"/> ≤ deviation &lt; <see cref="BlockPercent"/>: approval required;</item>
/// <item>deviation ≥ <see cref="BlockPercent"/>: block.</item>
/// </list>
/// This port computes nothing: the deviation and its band belong to the consuming backend calculator,
/// which UI and reports reuse; no percent is a code constant there.
/// </para>
/// </summary>
public sealed record SalesDeliveryTolerancePolicyV1(
    Guid CompanyId,
    ProfileVersionV1 Profile,
    decimal WarnPercent,
    decimal ApprovalPercent,
    decimal BlockPercent,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);

/// <summary>
/// A limit group's maximum discount percent, 0 ≤ value ≤ 100. What the percent is relative to (the
/// price base of the discount) is defined by the consuming contract (API-12), not by this port.
/// </summary>
public sealed record SalesDiscountLimitPolicyV1(
    Guid CompanyId,
    Guid LimitGroupId,
    ProfileVersionV1 Profile,
    decimal MaximumDiscountPercent,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);

/// <summary>
/// The order-approval amount threshold for a currency. <see cref="ThresholdAmountText"/> is null
/// exactly when <see cref="IsEnabled"/> is false; when enabled it is a lossless positive decimal
/// string whose scale the consumer checks against the currency's money profile.
/// </summary>
public sealed record SalesApprovalAmountThresholdPolicyV1(
    Guid CompanyId,
    Guid CurrencyId,
    ProfileVersionV1 Profile,
    bool IsEnabled,
    string? ThresholdAmountText,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);
