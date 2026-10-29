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
    /// asked about: no delivery tolerance, no discount limit for the limit group or no approval amount
    /// threshold for the currency. An empty id is <see cref="SalesPolicyRequestInvalid"/>, never this
    /// code. Kept apart from
    /// <c>settings_provider_unavailable</c> (the company has no Sales policy source) and
    /// <c>settings_profile_invalid</c> (malformed data). For a discount limit it is SAT-D2's
    /// "no limit defined", which routes every discount to approval; it never means "unlimited".
    /// </summary>
    public const string SalesPolicyNotFound = "settings_sales_policy_not_found";

    /// <summary>
    /// A Sales policy read was asked with an invalid identity (an empty limit group or currency id).
    /// Reported after the access check and before any lookup: it is the caller's error, never
    /// <see cref="SalesPolicyNotFound"/>, so an empty id can never be read as "no limit defined".
    /// </summary>
    public const string SalesPolicyRequestInvalid = "settings_sales_policy_request_invalid";

    /// <summary>
    /// SAT-D102: no e-Fatura scenario code list is in force for the company on the date. The key exists for every
    /// company and starts empty; a consumer reads this as "kaynak yok" (no per-customer override can be chosen or
    /// honoured), never as an implicit list.
    /// </summary>
    public const string EInvoiceScenarioCodeListNotFound = "settings_einvoice_scenario_code_list_not_found";

    /// <summary>
    /// SAT-D102: no default e-Fatura scenario is in force for the company on the date. The starting value is empty
    /// until Sinan enters it; a consumer without a customer override is then "unresolved" and must ask (fail-closed),
    /// never fall back to TEMEL, TİCARİ or any other value.
    /// </summary>
    public const string EInvoiceScenarioDefaultNotFound = "settings_einvoice_scenario_default_not_found";
}
