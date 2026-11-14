namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// F-029 S4b (URUN_F029 spec §5.4(1); URN-D2…D5 of <c>docs/decisions/SINAN_KARARLARI_2026-11-12_URUN.md</c>): the Settings
/// owner's names for maintaining the Sales delivery tolerance (SAT-D5) and the order-approval amount threshold (SAT-D6) as
/// persistent, versioned Settings records. Registered, NOT granted: no role, seed or user receives them here (a human grant
/// gate). Company-wide only. Not Critical: no approved source adds grant-time dual control.
/// </summary>
public static class SettingsSalesPolicyPermissionManifestV1
{
    /// <summary>Read the delivery tolerance timeline, transfer its compiled baseline once and record new versions.</summary>
    public const string DeliveryToleranceManage = "settings.sales-delivery-tolerances.manage";

    /// <summary>Read the approval amount thresholds, transfer their compiled baseline once and record new versions.</summary>
    public const string ApprovalThresholdManage = "settings.sales-approval-thresholds.manage";
}

/// <summary>The two policy families kept in Settings tables (the baseline transfer marker is per company and family).</summary>
public static class SalesPolicyFamiliesV1
{
    public const string DeliveryTolerance = "DeliveryTolerance";
    public const string ApprovalAmountThreshold = "ApprovalAmountThreshold";
}

/// <summary>Where a stored policy version comes from (URN spec §5.1 <c>SourceKind</c>).</summary>
public static class SalesPolicySourceKindsV1
{
    /// <summary>Copied unchanged from the reviewed compiled catalog by the one-time baseline transfer ("sürüm 0" source).</summary>
    public const string CompiledBaselineV0 = "CompiledBaselineV0";

    /// <summary>Recorded by an authorized person through the Settings command.</summary>
    public const string UserMaintained = "UserMaintained";
}

/// <summary>A version's place on the timeline relative to today (Europe/Istanbul).</summary>
public static class SalesPolicyVersionStatesV1
{
    public const string InForce = "InForce";
    public const string Scheduled = "Scheduled";
    public const string Ended = "Ended";

    /// <summary>A later version starts on the same date, so this one never applies.</summary>
    public const string Superseded = "Superseded";
}

/// <summary>Technical bounds of the inputs (storage and transport shape, not policy).</summary>
public static class SalesPolicyInputLimitsV1
{
    /// <summary>SAT-D68 precedent: the mandatory reason of every change.</summary>
    public const int ReasonMaximumLength = 4000;

    /// <summary>The decision reference a baseline row carries (the catalog entry's source text).</summary>
    public const int DecisionReferenceMaximumLength = 400;

    /// <summary>Decimal places a tolerance percent may carry; a longer value is refused, never rounded.</summary>
    public const int PercentMaximumScale = 4;

    /// <summary>Integer digits of a threshold amount (storage shape decimal(28,8)).</summary>
    public const int AmountMaximumIntegerDigits = 20;

    /// <summary>Fraction digits a threshold amount can be stored with; the currency's money profile scale bounds it further.</summary>
    public const int AmountMaximumScale = 8;
}

/// <summary>Published problem codes of the Sales policy routes.</summary>
public static class SalesPolicyProblemCodesV1
{
    public const string RequestInvalid = "settings_sales_policy_request_invalid";
    public const string CommandMetadataInvalid = "settings_sales_policy_command_metadata_invalid";
    public const string ReasonRequired = "settings_sales_policy_reason_required";
    public const string ReasonTooLong = "settings_sales_policy_reason_too_long";
    public const string IdempotencyConflict = "settings_sales_policy_idempotency_conflict";
    public const string VersionConflict = "settings_sales_policy_version_conflict";
    public const string EffectiveFromPast = "settings_sales_policy_effective_from_past";

    /// <summary>A version is recorded before the family's compiled baseline was transferred.</summary>
    public const string BaselineRequired = "settings_sales_policy_baseline_required";

    /// <summary>The family's compiled baseline was already transferred (the transfer is one-time).</summary>
    public const string AlreadyTransferred = "settings_sales_policy_baseline_already_transferred";

    /// <summary>The currency has no active money profile covering the date, so no threshold can be expressed in it.</summary>
    public const string CurrencyNotServed = "settings_sales_policy_currency_not_served";

    /// <summary>The threshold amount carries more fraction digits than the currency's money profile scale.</summary>
    public const string AmountScaleExceeded = "settings_sales_policy_amount_scale_exceeded";

    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>
/// The one-time transfer of a family's reviewed compiled catalog entries for the server-resolved company into the Settings
/// table, unchanged (same profile id, version, values, active flag and window), with <see cref="SalesPolicySourceKindsV1.CompiledBaselineV0"/>
/// and the catalog source as the decision reference. After it, the table is the family's only source.
/// </summary>
public sealed record TransferSalesPolicyBaselineRequestV1(string? Reason);

/// <summary>
/// A new delivery tolerance version: the three percents as lossless text (0 ≤ warn ≤ approval ≤ block ≤ 100, at most
/// <see cref="SalesPolicyInputLimitsV1.PercentMaximumScale"/> decimals), starting on <c>EffectiveFrom</c> (omitted = today,
/// Europe/Istanbul; earlier is refused, URN-D4). A version on the same date as an existing one replaces it from that date.
/// <c>ExpectedVersion</c> is the latest stored version ("0" when none).
/// </summary>
public sealed record RecordSalesDeliveryToleranceRequestV1(
    string? ExpectedVersion,
    string? WarnPercent,
    string? ApprovalPercent,
    string? BlockPercent,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// A new approval amount threshold version for one currency: enabled with a positive amount as lossless text (no leading
/// zero, scale within the currency's money profile) or disabled with no amount (no amount-based routing).
/// </summary>
public sealed record RecordSalesApprovalThresholdRequestV1(
    string? ExpectedVersion,
    bool? IsEnabled,
    string? Amount,
    string? Reason,
    DateOnly? EffectiveFrom = null);

public sealed record SalesDeliveryToleranceVersionViewV1(
    Guid ProfileId,
    string Version,
    string WarnPercent,
    string ApprovalPercent,
    string BlockPercent,
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
/// The company's delivery tolerance timeline. Before the baseline transfer the versions are the compiled catalog's
/// (<c>IsBaselineTransferred</c> false, no recorder); afterwards the stored ones. <c>Version</c> is the latest stored version
/// ("0" when none) — the next record's expected version.
/// </summary>
public sealed record SalesDeliveryToleranceViewV1(
    Guid CompanyId,
    DateOnly Today,
    bool IsBaselineTransferred,
    string Version,
    IReadOnlyList<SalesDeliveryToleranceVersionViewV1> Versions);

public sealed record SalesApprovalThresholdVersionViewV1(
    Guid ProfileId,
    string Version,
    bool IsEnabled,
    string? Amount,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    string State,
    string SourceKind,
    string? DecisionReference,
    string? RecordedBy,
    DateTimeOffset? RecordedAtUtc,
    string? Reason);

public sealed record SalesApprovalThresholdCurrencyViewV1(
    Guid CurrencyId,
    string Version,
    IReadOnlyList<SalesApprovalThresholdVersionViewV1> Versions);

/// <summary>The company's approval amount thresholds per currency (same source rules as the tolerance view).</summary>
public sealed record SalesApprovalThresholdsViewV1(
    Guid CompanyId,
    DateOnly Today,
    bool IsBaselineTransferred,
    IReadOnlyList<SalesApprovalThresholdCurrencyViewV1> Currencies);

/// <summary>A command's terminal answer (stored for replay). <c>Code</c> is null for a success.</summary>
public sealed record SalesPolicyCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record SalesPolicyCommandResultV1(SalesPolicyCommandAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>
/// The Sales policy administration (F-029 S4b). Every read and command is authorized first on the family's manage name
/// company-wide in the server-resolved company and re-measured before every save. <see cref="UnauthorizedAccessException"/>
/// means 403. One person decides (SAT-D68 precedent): a mandatory reason, Idempotency-Key, expected version and one audit
/// event per real change (none for a no-op).
/// </summary>
public interface ISettingsSalesPolicyAdministrationV1
{
    Task<SalesDeliveryToleranceViewV1> GetDeliveryToleranceAsync(CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> TransferDeliveryToleranceBaselineAsync(TransferSalesPolicyBaselineRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> RecordDeliveryToleranceAsync(RecordSalesDeliveryToleranceRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SalesApprovalThresholdsViewV1> GetApprovalThresholdsAsync(CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> TransferApprovalThresholdBaselineAsync(TransferSalesPolicyBaselineRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SalesPolicyCommandResultV1> RecordApprovalThresholdAsync(Guid currencyId, RecordSalesApprovalThresholdRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
