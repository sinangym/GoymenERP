using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>FNS-D31/D70, FNS-D68 and KLN-D109: registration only; no role or user is granted this name here.</summary>
public static class SettingsCollateralTypePermissionsV1
{
    public const string Manage = "settings.collateral-types.manage";
}

/// <summary>The eligible classes decided by FNS-D31. Cheques and notes are documentary risk, never collateral.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SettingsCollateralKindV1>))]
public enum SettingsCollateralKindV1
{
    BankGuaranteedPayment = 1,
    Dbs = 2,
    GuaranteeLetter = 3,
    CreditCard = 4,
    Vinov = 5,
    Mortgage = 6,
}

public static class SettingsCollateralTypeProblemCodesV1
{
    public const string RequestInvalid = "settings_collateral_type_request_invalid";
    public const string CommandMetadataInvalid = "settings_collateral_type_command_metadata_invalid";
    public const string ReasonRequired = "settings_collateral_type_reason_required";
    public const string ReasonTooLong = "settings_collateral_type_reason_too_long";
    public const string CodeConflict = "settings_collateral_type_code_conflict";
    public const string EntryNotFound = "settings_collateral_type_not_found";
    public const string IdempotencyConflict = "settings_collateral_type_idempotency_conflict";
    public const string VersionConflict = "settings_collateral_type_version_conflict";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string ProfileInvalid = "settings_profile_invalid";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>No seed. Explicit creation records the decided initial rate 100; the rate is never inferred from a missing row.</summary>
public sealed record CreateSettingsCollateralTypeRequestV1(string? Code, string? Name, SettingsCollateralKindV1? Kind, string? Reason);

/// <summary>Append-only change. RatePercent is exact invariant decimal text in [0,100], no rounding. Code/kind stay fixed.</summary>
public sealed record RecordSettingsCollateralTypeRequestV1(string? ExpectedVersion, string? Name, bool? Active,
    string? RatePercent, string? Reason);

public sealed record SettingsCollateralTypeViewV1(Guid Id, string Code, string Name, SettingsCollateralKindV1 Kind,
    bool Active, string RatePercent, string Version, string RecordedBy, DateTimeOffset RecordedAtUtc);

public sealed record SettingsCollateralTypesViewV1(Guid CompanyId, IReadOnlyList<SettingsCollateralTypeViewV1> Entries);
public sealed record SettingsCollateralTypeCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);
public sealed record SettingsCollateralTypeCommandResultV1(SettingsCollateralTypeCommandAnswerV1 Answer, bool IsReplay,
    string OriginalCorrelationId);

public interface ISettingsCollateralTypeAdministrationV1
{
    Task<SettingsCollateralTypesViewV1> GetAsync(CancellationToken cancellationToken = default);
    Task<SettingsCollateralTypeCommandResultV1> CreateAsync(CreateSettingsCollateralTypeRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsCollateralTypeCommandResultV1> RecordAsync(Guid entryId, RecordSettingsCollateralTypeRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

/// <summary>Consumer read under its own permission. Returns all current types, including inactive types held by old records.
/// No historical/future-effective editing policy is introduced; the Finance caller supplies its business day, not an effective-date override.</summary>
public interface ISettingsCollateralTypeCatalogV1
{
    Task<SettingsCollateralTypesViewV1> ListAsync(Guid companyId, CancellationToken cancellationToken = default);
}
