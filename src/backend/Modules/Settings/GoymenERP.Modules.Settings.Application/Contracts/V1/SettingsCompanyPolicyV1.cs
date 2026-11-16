namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// B-13 (ADR-SET-002): company-wide policy keys kept as insert-only, versioned Settings records. One registry, one version
/// sequence per company and key; a missing record is never read as a permissive value.
/// </summary>
public static class SettingsCompanyPolicyPermissionManifestV1
{
    /// <summary>
    /// Opening or closing the Finance opening-balance period. Registered here only; granted to no role (human gate,
    /// NEEDS-SINAN: who may open/close the opening period).
    /// </summary>
    public const string FinanceOpeningPeriodManage = "settings.finance-opening-period.manage";
}

/// <summary>The stored keys of the company policy registry.</summary>
public static class CompanyPolicyKeysV1
{
    /// <summary>The Finance opening-balance period (open/closed).</summary>
    public const string FinanceOpeningPeriod = "finance.opening-period";

    /// <summary>URT-D96/URT-D100: the company default due-date buffer in working days.</summary>
    public const string ManufacturingDueDateBuffer = "manufacturing.due-date-buffer";

    /// <summary>SAT-D381: the default subcontract VAT/withholding code on the sales side.</summary>
    public const string SalesSubcontractTaxDefault = "sales.subcontract-tax-default";

    /// <summary>ALS-D105: the separate purchasing-side record (read only until its write authority is decided).</summary>
    public const string PurchasingSubcontractTaxDefault = "purchasing.subcontract-tax-default";

    public const int MaximumLength = 64;

    public static IReadOnlyList<string> All { get; } =
    [
        FinanceOpeningPeriod,
        ManufacturingDueDateBuffer,
        SalesSubcontractTaxDefault,
        PurchasingSubcontractTaxDefault,
    ];
}

public static class CompanyPolicyInputLimitsV1
{
    public const int ReasonMaximumLength = 4000;

    /// <summary>URT-D100: the decided start value; no record means this value (nothing is seeded).</summary>
    public const int DefaultDueDateBufferWorkingDays = 0;
}

public static class CompanyPolicyProblemCodesV1
{
    public const string RequestInvalid = "settings_company_policy_request_invalid";
    public const string CommandMetadataInvalid = "settings_company_policy_command_metadata_invalid";
    public const string ReasonRequired = "settings_company_policy_reason_required";
    public const string ReasonTooLong = "settings_company_policy_reason_too_long";
    public const string IdempotencyConflict = "settings_company_policy_idempotency_conflict";
    public const string VersionConflict = "settings_company_policy_version_conflict";
    public const string ExemptionCodeUnknown = "settings_company_policy_exemption_code_unknown";
    public const string WithholdingCodeUnknown = "settings_company_policy_withholding_code_unknown";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
    public const string ProfileInvalid = "settings_profile_invalid";
}

/// <summary>The opening period as Settings stores it. <see cref="NotRecorded"/> is never read as open.</summary>
public enum FinanceOpeningPeriodStateV1
{
    NotRecorded = 0,
    Open = 1,
    Closed = 2,
}

public sealed record RecordFinanceOpeningPeriodRequestV1(string? ExpectedVersion, bool? Open, string? Reason);

public sealed record RecordManufacturingDueDateBufferRequestV1(string? ExpectedVersion, int? WorkingDays, string? Reason);

/// <summary>Percent values are decimal text (≤ 100, ≤ 4 decimals); codes are UBL-TR list codes checked against the tax master.</summary>
public sealed record RecordSubcontractTaxDefaultRequestV1(
    string? ExpectedVersion,
    bool? VatApplies,
    string? VatPercent,
    string? ExemptionCode,
    bool? WithholdingApplies,
    string? WithholdingPercent,
    string? WithholdingCode,
    string? Reason);

public sealed record FinanceOpeningPeriodVersionViewV1(
    string Version,
    bool Open,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

public sealed record FinanceOpeningPeriodViewV1(
    Guid CompanyId,
    FinanceOpeningPeriodStateV1 State,
    string Version,
    IReadOnlyList<FinanceOpeningPeriodVersionViewV1> Versions);

public sealed record ManufacturingDueDateBufferVersionViewV1(
    string Version,
    int WorkingDays,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

public sealed record ManufacturingDueDateBufferViewV1(
    Guid CompanyId,
    int WorkingDays,
    string Version,
    IReadOnlyList<ManufacturingDueDateBufferVersionViewV1> Versions);

public sealed record SubcontractTaxDefaultValueViewV1(
    bool VatApplies,
    string? VatPercent,
    string? ExemptionCode,
    bool WithholdingApplies,
    string? WithholdingPercent,
    string? WithholdingCode);

public sealed record SubcontractTaxDefaultVersionViewV1(
    string Version,
    SubcontractTaxDefaultValueViewV1 Value,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary><see cref="Current"/> is <c>null</c> while nothing is recorded (no default; each line chooses).</summary>
public sealed record SubcontractTaxDefaultViewV1(
    Guid CompanyId,
    SubcontractTaxDefaultValueViewV1? Current,
    string Version,
    IReadOnlyList<SubcontractTaxDefaultVersionViewV1> Versions);

public sealed record CompanyPolicyCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);

public sealed record CompanyPolicyCommandResultV1(
    CompanyPolicyCommandAnswerV1 Answer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// The K3 administration of the company policy registry: each key behind its own company-wide permission, a mandatory
/// reason, one Idempotency-Key, the expected version as decimal text and one audit event per real change.
/// </summary>
public interface ISettingsCompanyPolicyAdministrationV1
{
    Task<FinanceOpeningPeriodViewV1> GetFinanceOpeningPeriodAsync(CancellationToken cancellationToken = default);

    Task<CompanyPolicyCommandResultV1> RecordFinanceOpeningPeriodAsync(RecordFinanceOpeningPeriodRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<ManufacturingDueDateBufferViewV1> GetManufacturingDueDateBufferAsync(CancellationToken cancellationToken = default);

    Task<CompanyPolicyCommandResultV1> RecordManufacturingDueDateBufferAsync(RecordManufacturingDueDateBufferRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);

    Task<SubcontractTaxDefaultViewV1> GetSalesSubcontractTaxDefaultAsync(CancellationToken cancellationToken = default);

    Task<CompanyPolicyCommandResultV1> RecordSalesSubcontractTaxDefaultAsync(RecordSubcontractTaxDefaultRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// PANO row 64 (draft): the finance policy keys Finance reads. <see cref="OpeningPeriodOpen"/> is <c>true</c> only for a
/// stored "open" version; <see cref="Version"/> is 0 while nothing is recorded and grows by one on every open or close.
/// </summary>
public sealed record SettingsFinancePolicyV1(
    Guid CompanyId,
    FinanceOpeningPeriodStateV1 OpeningPeriod,
    long Version)
{
    public bool OpeningPeriodOpen => OpeningPeriod == FinanceOpeningPeriodStateV1.Open;

    /// <summary>The version text handed to Finance: unique per key and version, so it changes on every open or close.</summary>
    public string VersionText => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"{CompanyPolicyKeysV1.FinanceOpeningPeriod}:{Version}");
}

public interface ISettingsFinancePolicyProviderV1
{
    /// <summary>
    /// Fails with <see cref="SettingsPrerequisiteException"/> (<c>settings_access_denied</c>,
    /// <c>settings_provider_unavailable</c>, <c>settings_profile_invalid</c>); never answers a permissive fallback.
    /// </summary>
    Task<SettingsFinancePolicyV1> GetFinancePolicyAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>URT-D96/URT-D100: the company default; a work-center value replaces it (never added to it).</summary>
public sealed record SettingsManufacturingDueDateBufferV1(Guid CompanyId, int WorkingDays, long Version);

public interface ISettingsManufacturingDueDateBufferProviderV1
{
    Task<SettingsManufacturingDueDateBufferV1> GetDueDateBufferAsync(Guid companyId,
        CancellationToken cancellationToken = default);
}

public enum SubcontractTaxSideV1
{
    Sales = 1,
    Purchasing = 2,
}

public sealed record SettingsSubcontractTaxDefaultV1(
    bool VatApplies,
    decimal? VatPercent,
    string? ExemptionCode,
    bool WithholdingApplies,
    decimal? WithholdingPercent,
    string? WithholdingCode);

/// <summary><see cref="Default"/> is <c>null</c> while the side has no recorded default (version 0).</summary>
public sealed record SettingsSubcontractTaxDefaultAnswerV1(
    Guid CompanyId,
    SubcontractTaxSideV1 Side,
    SettingsSubcontractTaxDefaultV1? Default,
    long Version);

public interface ISettingsSubcontractTaxDefaultProviderV1
{
    Task<SettingsSubcontractTaxDefaultAnswerV1> GetSubcontractTaxDefaultAsync(Guid companyId, SubcontractTaxSideV1 side,
        CancellationToken cancellationToken = default);
}

/// <summary>FNS-D97: what a credit posting needs an account for (granularity NEEDS-SINAN).</summary>
public enum FinanceCreditAccountPurposeV1
{
    CreditDrawdown = 1,
    CreditRepayment = 2,
    CreditInterestAccrual = 3,
}

public enum FinanceCreditAccountMappingStatusV1
{
    /// <summary>"eşleme eksik": no account is mapped; the item stays unposted.</summary>
    MappingMissing = 0,
    Mapped = 1,
}

public sealed record SettingsFinanceCreditAccountMappingV1(
    Guid CompanyId,
    FinanceCreditAccountPurposeV1 Purpose,
    FinanceCreditAccountMappingStatusV1 Status,
    string? LedgerAccountCode,
    long Version);

/// <summary>FNS-D97: the read port only; no write path exists yet, so every purpose answers "eşleme eksik" until one does.</summary>
public interface ISettingsFinanceCreditAccountMappingProviderV1
{
    Task<SettingsFinanceCreditAccountMappingV1> GetCreditAccountMappingAsync(Guid companyId,
        FinanceCreditAccountPurposeV1 purpose, CancellationToken cancellationToken = default);
}

/// <summary>A number series key (draft; the series catalog is NEEDS-SINAN N-12).</summary>
public sealed record SettingsNumberSeriesKeyV1(string Value);

public sealed record SettingsNumberSeriesNumberV1(Guid CompanyId, SettingsNumberSeriesKeyV1 SeriesKey, string Number);

/// <summary>
/// PANO row 64 draft contract only: no implementation is registered until the series format, reset and gap policy are
/// decided (NEEDS-SINAN N-12). A consumer must not assume a number exists.
/// </summary>
public interface ISettingsNumberSeriesProviderV1
{
    Task<SettingsNumberSeriesNumberV1> NextNumberAsync(Guid companyId, SettingsNumberSeriesKeyV1 seriesKey,
        CancellationToken cancellationToken = default);
}
