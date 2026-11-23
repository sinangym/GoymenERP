namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>URN-D2/D3, KIM-D9: registered only; no role or actor is granted the ability to change money profiles.</summary>
public static class SettingsMoneyProfilePermissionManifestV1
{
    public const string Manage = "settings.money-profiles.manage";
}

public static class MoneyProfileInputLimitsV1
{
    public const int MaximumScale = 28;
    public const int ReasonMaximumLength = 4000;
}

public static class MoneyProfileProblemCodesV1
{
    public const string RequestInvalid = "settings_money_profile_request_invalid";
    public const string CommandMetadataInvalid = "settings_money_profile_command_metadata_invalid";
    public const string ReasonRequired = "settings_money_profile_reason_required";
    public const string ReasonTooLong = "settings_money_profile_reason_too_long";
    public const string VersionConflict = "settings_money_profile_version_conflict";
    public const string IdempotencyConflict = "settings_money_profile_idempotency_conflict";
    public const string EffectiveFromPast = "settings_money_profile_effective_from_past";
    public const string ProfileUnknown = "settings_money_profile_unknown";
    public const string ProviderUnavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
}

/// <summary>The registered operation/currency identity is selected. ExpectedVersion "0" records
/// the first human-maintained profile; no monetary defaults or source identity are invented (URN-D2/D3/D4).</summary>
public sealed record RecordMoneyProfileRequestV1(string? ExpectedVersion, Guid? CurrencyId, string? Operation,
    int? Scale, int? UnitPriceScale, bool? IsActive, string? Reason, DateOnly? EffectiveFrom = null);

public sealed record MoneyProfileVersionViewV1(Guid ProfileId, string Version, int Scale, int? UnitPriceScale,
    bool IsActive, DateOnly EffectiveFrom, DateOnly? EffectiveThrough, string State, string SourceKind,
    string? DecisionReference, string? RecordedBy, DateTimeOffset? RecordedAtUtc, string? Reason);

public sealed record MoneyProfileSeriesViewV1(Guid CurrencyId, string CurrencyCode, string Operation, string Version,
    IReadOnlyList<MoneyProfileVersionViewV1> Versions);

public sealed record MoneyProfileViewV1(Guid CompanyId, DateOnly Today, IReadOnlyList<MoneyProfileSeriesViewV1> Profiles);
public sealed record MoneyProfileCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail);
public sealed record MoneyProfileCommandResultV1(MoneyProfileCommandAnswerV1 Answer, bool IsReplay, string OriginalCorrelationId);

/// <summary>URN-D3/4: one authorized publisher; a reasoned, idempotent new version applies prospectively only.</summary>
public interface ISettingsMoneyProfileAdministrationV1
{
    Task<MoneyProfileViewV1> GetAsync(CancellationToken cancellationToken = default);
    Task<MoneyProfileCommandResultV1> RecordAsync(RecordMoneyProfileRequestV1 request, SettingsCommandMetadataV1 metadata,
        CancellationToken cancellationToken = default);
}
