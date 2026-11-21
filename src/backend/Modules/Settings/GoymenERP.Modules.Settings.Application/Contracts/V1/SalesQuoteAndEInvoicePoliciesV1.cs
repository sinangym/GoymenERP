namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// F-029 S5/S6 (URUN_F029 spec §5.4(2); URN-D2…D5 of <c>docs/decisions/SINAN_KARARLARI_2026-11-12_URUN.md</c>): the Settings
/// owner's names for maintaining the Sales quote policies (SAT-D25 loss reasons, SAT-D39 default validity) and the e-Fatura
/// scenario policy (SAT-D102 code list and default scenario) as persistent, versioned Settings records. Registered, NOT
/// granted: no role, seed or user receives them here (a human grant gate). Company-wide only. Not Critical: no approved
/// source adds grant-time dual control.
/// </summary>
public static class SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1
{
    /// <summary>Read the quote policy timelines, transfer their compiled baseline once and record new versions.</summary>
    public const string SalesQuotePolicyManage = "settings.sales-quote-policies.manage";

    /// <summary>Read the e-Fatura scenario timelines, transfer their compiled baseline once and record new versions.</summary>
    public const string EInvoiceScenarioManage = "settings.einvoice-scenarios.manage";
}

/// <summary>Technical bounds of the quote and e-Fatura scenario inputs (storage and transport shape, not policy).</summary>
public static class SalesQuoteAndEInvoicePolicyInputLimitsV1
{
    /// <summary>The stored length of a loss reason code (the compiled catalog's bound).</summary>
    public const int LossReasonCodeMaximumLength = ApprovedSalesQuotePolicyCatalog.MaximumReasonCodeLength;

    /// <summary>The stored length of a loss reason label (the compiled catalog's bound).</summary>
    public const int LossReasonLabelMaximumLength = ApprovedSalesQuotePolicyCatalog.MaximumReasonLabelLength;

    /// <summary>Entries one loss reason list version may carry (a request size bound).</summary>
    public const int LossReasonMaximumCount = 100;

    /// <summary>Codes one e-Fatura scenario code list version may carry (a request size bound).</summary>
    public const int ScenarioCodeMaximumCount = 100;
}

/// <summary>The problem codes the quote and e-Fatura scenario routes add to <see cref="SalesPolicyProblemCodesV1"/>.</summary>
public static class SalesQuoteAndEInvoicePolicyProblemCodesV1
{
    /// <summary>
    /// A loss reason identity is broken: a given reason id was never stored for the company, a stored reason would get
    /// another code, or a code already belongs to another reason id of the company (a quote keeps the id it recorded and
    /// reports group by the code, so both stay stable).
    /// </summary>
    public const string LossReasonIdentityConflict = "settings_sales_policy_loss_reason_identity_conflict";

    /// <summary>
    /// A default e-Fatura scenario would not be one of the codes of the list in force on a date it covers (a new default
    /// outside the list, or a new list that drops a stored default's code).
    /// </summary>
    public const string EInvoiceDefaultNotInList = "settings_sales_policy_einvoice_default_not_in_list";
}

/// <summary>
/// One loss reason of a new list version. <c>ReasonId</c> null adds a new reason (the server mints its id); a given id keeps
/// a stored reason (its code must stay the same, its label and flags may change). A reason left out of the new list is not
/// offered from the new version on; quotes that recorded it keep its meaning through the version they recorded.
/// </summary>
public sealed record RecordSalesQuoteLossReasonInputV1(
    Guid? ReasonId,
    string? Code,
    string? Label,
    bool? RequiresExplanation,
    bool? IsActive);

/// <summary>
/// A new loss reason list version (SAT-D25): a non-empty, ordered list with distinct ids and codes, starting on
/// <c>EffectiveFrom</c> (omitted = today, Europe/Istanbul; earlier is refused, URN-D4). <c>ExpectedVersion</c> is the latest
/// stored list version ("0" when none).
/// </summary>
public sealed record RecordSalesQuoteLossReasonsRequestV1(
    string? ExpectedVersion,
    IReadOnlyList<RecordSalesQuoteLossReasonInputV1>? LossReasons,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>A new default quote validity version (SAT-D39): a positive number of days, from its business date.</summary>
public sealed record RecordSalesQuoteDefaultValidityRequestV1(
    string? ExpectedVersion,
    int? ValidityDays,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// A new e-Fatura scenario code list version (SAT-D102): a non-empty, ordered list of distinct, well-formed codes
/// (<see cref="EInvoiceScenarioCodeFormatV1"/>). A stored default whose window reaches the new list's dates must stay in it.
/// </summary>
public sealed record RecordEInvoiceScenarioCodeListRequestV1(
    string? ExpectedVersion,
    IReadOnlyList<string?>? Codes,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// A new default e-Fatura scenario version (SAT-D102: Sinan enters it): one code of every list in force over the dates the
/// new default covers.
/// </summary>
public sealed record RecordEInvoiceScenarioDefaultRequestV1(
    string? ExpectedVersion,
    string? ScenarioCode,
    string? Reason,
    DateOnly? EffectiveFrom = null);

public sealed record SalesQuoteLossReasonViewV1(
    Guid ReasonId,
    string Code,
    string Label,
    bool RequiresExplanation,
    bool IsActive);

public sealed record SalesQuoteLossReasonListVersionViewV1(
    Guid ProfileId,
    string Version,
    IReadOnlyList<SalesQuoteLossReasonViewV1> LossReasons,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string State,
    string SourceKind,
    string? DecisionReference,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    string? Reason);

public sealed record SalesQuoteDefaultValidityVersionViewV1(
    Guid ProfileId,
    string Version,
    int ValidityDays,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string State,
    string SourceKind,
    string? DecisionReference,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    string? Reason);

/// <summary>
/// The company's quote policies: two keys with their own timelines and versions. Before the baseline transfer the versions
/// are the compiled catalog's (<c>IsBaselineTransferred</c> false, no recorder); afterwards the stored ones. Each
/// <c>…Version</c> is the key's latest stored version ("0" when none) — the next record's expected version.
/// </summary>
public sealed record SalesQuotePolicyViewV1(
    Guid CompanyId,
    DateOnly Today,
    bool IsBaselineTransferred,
    string LossReasonListVersion,
    IReadOnlyList<SalesQuoteLossReasonListVersionViewV1> LossReasonLists,
    string DefaultValidityVersion,
    IReadOnlyList<SalesQuoteDefaultValidityVersionViewV1> DefaultValidities);

public sealed record EInvoiceScenarioCodeListVersionViewV1(
    Guid ProfileId,
    string Version,
    IReadOnlyList<string> Codes,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string State,
    string SourceKind,
    string? DecisionReference,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    string? Reason);

public sealed record EInvoiceScenarioDefaultVersionViewV1(
    Guid ProfileId,
    string Version,
    string ScenarioCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string State,
    string SourceKind,
    string? DecisionReference,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    string? Reason);

/// <summary>
/// The company's e-Fatura scenario policy: the code list and the default scenario, two keys with their own timelines (same
/// source rules as <see cref="SalesQuotePolicyViewV1"/>). An empty <c>Defaults</c> is the SAT-D102 starting state.
/// </summary>
public sealed record EInvoiceScenarioPolicyViewV1(
    Guid CompanyId,
    DateOnly Today,
    bool IsBaselineTransferred,
    string CodeListVersion,
    IReadOnlyList<EInvoiceScenarioCodeListVersionViewV1> CodeLists,
    string DefaultVersion,
    IReadOnlyList<EInvoiceScenarioDefaultVersionViewV1> Defaults);

/// <summary>
/// The Sales quote policy administration (F-029 S5). Every read and command is authorized first on
/// <see cref="SettingsSalesQuoteAndEInvoicePolicyPermissionManifestV1.SalesQuotePolicyManage"/> company-wide in the
/// server-resolved company and re-measured before every save. <see cref="UnauthorizedAccessException"/> means 403. One person
/// decides (URN-D3): a mandatory reason, Idempotency-Key, expected version and one audit event per real change (none for a
/// no-op).
/// </summary>
public interface ISettingsSalesQuotePolicyAdministrationV1
{
    Task<SalesQuotePolicyViewV1> GetSalesQuotePolicyAsync(CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> TransferSalesQuotePolicyBaselineAsync(TransferSalesPolicyBaselineRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> RecordSalesQuoteLossReasonsAsync(RecordSalesQuoteLossReasonsRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> RecordSalesQuoteDefaultValidityAsync(RecordSalesQuoteDefaultValidityRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>The e-Fatura scenario policy administration (F-029 S6); the same command rules as the quote administration.</summary>
public interface ISettingsEInvoiceScenarioPolicyAdministrationV1
{
    Task<EInvoiceScenarioPolicyViewV1> GetEInvoiceScenarioPolicyAsync(CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> TransferEInvoiceScenarioBaselineAsync(TransferSalesPolicyBaselineRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> RecordEInvoiceScenarioCodeListAsync(RecordEInvoiceScenarioCodeListRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> RecordEInvoiceScenarioDefaultAsync(RecordEInvoiceScenarioDefaultRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
