namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Published <see cref="SettingsPrerequisiteException.Code"/> values a consumer may map to its own
/// public answer. A consumer maps these constants, never a provider class and never message text.
/// </summary>
public static class SettingsPrerequisiteCodesV1
{
    /// <summary>
    /// The company's approved source is reachable but holds no profile for the unit asked about
    /// (including an empty unit id). Kept apart from <c>settings_provider_unavailable</c> (there is
    /// no source to ask) and <c>settings_profile_invalid</c> (the source's data is malformed): it is
    /// a permanent answer about this identity, not a transient failure.
    /// </summary>
    public const string UnitProfileNotFound = "settings_unit_profile_not_found";

    /// <summary>
    /// The company's approved money source is reachable, and the operation is approved, but it holds
    /// no profile for the currency asked about (for example USD or EUR for GDC today, or an empty
    /// currency id). Kept apart from <c>settings_provider_unavailable</c> (no source, or the
    /// operation is not an approved one) and <c>settings_profile_invalid</c> (malformed data): a
    /// permanent answer about this currency, not a transient failure.
    /// </summary>
    public const string MoneyProfileNotFound = "settings_money_profile_not_found";

    /// <summary>
    /// A reachable Settings source has no Manufacturing tolerance profile for the exact company
    /// and canonical process. It is distinct from an absent source and from malformed catalog data.
    /// </summary>
    public const string ManufacturingToleranceProfileNotFound =
        "settings_manufacturing_tolerance_profile_not_found";

    /// <summary>
    /// The company's approved Sales policy source is reachable but holds no policy for the identity
    /// asked about: no delivery tolerance, no discount limit for the limit group (including an empty
    /// id) or no approval amount threshold for the currency. Kept apart from
    /// <c>settings_provider_unavailable</c> (the company has no Sales policy source) and
    /// <c>settings_profile_invalid</c> (malformed data). For a discount limit it is SAT-D2's
    /// "no limit defined", which routes every discount to approval; it never means "unlimited".
    /// </summary>
    public const string SalesPolicyNotFound = "settings_sales_policy_not_found";
}
