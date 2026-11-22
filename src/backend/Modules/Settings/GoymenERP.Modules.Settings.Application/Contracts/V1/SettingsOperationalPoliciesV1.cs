namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

public static class SettingsOperationalPolicyPermissionsV1
{
    // Technical registration only. Real grant/role mapping remains human-owned.
    public const string ScrapCostManage = "settings.manufacturing-scrap-cost.manage";
    public const string NotificationRetentionManage = "settings.notification-retention.manage";
    /// <summary>
    /// YOL-D17/YOL-D18 (W1-A): records the two approval duration class lengths. Registered, not granted; company-wide;
    /// not Critical. Giving it to a role is a human gate.
    /// </summary>
    public const string WorkApprovalDurationManage = "settings.work-approval-duration.manage";
}

public sealed record SettingsManufacturingScrapCostPolicyV1(Guid CompanyId, bool IncludeScrapCost, long Version)
{
    public string VersionText => $"manufacturing.include-scrap-cost:{Version.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
public interface ISettingsManufacturingScrapCostPolicyProviderV1
{
    Task<SettingsManufacturingScrapCostPolicyV1> GetScrapCostPolicyAsync(Guid companyId, CancellationToken cancellationToken = default);
}
public sealed record SettingsNotificationRetentionPolicyV1(Guid CompanyId, int ActiveDays, long Version)
{
    public string VersionText => $"work.notification-active-days:{Version.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
public interface ISettingsNotificationRetentionPolicyProviderV1
{
    Task<SettingsNotificationRetentionPolicyV1> GetNotificationRetentionAsync(Guid companyId, CancellationToken cancellationToken = default);
}
/// <summary>
/// YOL-D18 (W1-A): the lengths of the two approval duration classes. A null length is "not recorded": the reader shows
/// "süre tanımlı değil" and never assumes a default (fail closed). Each length carries its own version text.
/// </summary>
public sealed record SettingsWorkApprovalDurationPolicyV1(Guid CompanyId, int? Hours, long HoursVersion, int? BusinessDays,
    long BusinessDaysVersion)
{
    public string HoursVersionText => $"work.approval-duration-hours:{HoursVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    public string BusinessDaysVersionText =>
        $"work.approval-duration-business-days:{BusinessDaysVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
public interface ISettingsWorkApprovalDurationPolicyProviderV1
{
    Task<SettingsWorkApprovalDurationPolicyV1> GetWorkApprovalDurationAsync(Guid companyId, CancellationToken cancellationToken = default);
}
public static class SettingsWorkApprovalDurationLimitsV1
{
    /// <summary>Input range only (one hour up to a week of hours); the decided values live in recorded versions.</summary>
    public const int MaximumHours = 168;
    /// <summary>Input range only; the decided values live in recorded versions.</summary>
    public const int MaximumBusinessDays = 30;
}
public sealed record RecordWorkApprovalDurationRequestV1(string? ExpectedVersion, int? Value, string? Reason);
public sealed record WorkApprovalDurationClassViewV1(int? Value, string Version,
    IReadOnlyList<WholeNumberCompanyPolicyVersionViewV1> Versions);
public sealed record WorkApprovalDurationPolicyViewV1(Guid CompanyId, WorkApprovalDurationClassViewV1 Hours,
    WorkApprovalDurationClassViewV1 BusinessDays);
public sealed record RecordManufacturingScrapCostRequestV1(string? ExpectedVersion, bool? IncludeScrapCost, string? Reason);
public sealed record RecordNotificationRetentionRequestV1(string? ExpectedVersion, int? ActiveDays, string? Reason);
public sealed record BooleanCompanyPolicyVersionViewV1(string Version, bool Value, string RecordedBy, DateTimeOffset RecordedAtUtc, string Reason);
public sealed record WholeNumberCompanyPolicyVersionViewV1(string Version, int Value, string RecordedBy, DateTimeOffset RecordedAtUtc, string Reason);
public sealed record ManufacturingScrapCostPolicyViewV1(Guid CompanyId, bool IncludeScrapCost, string Version,
    IReadOnlyList<BooleanCompanyPolicyVersionViewV1> Versions);
public sealed record NotificationRetentionPolicyViewV1(Guid CompanyId, int ActiveDays, string Version,
    IReadOnlyList<WholeNumberCompanyPolicyVersionViewV1> Versions);
public interface ISettingsOperationalPolicyAdministrationV1
{
    Task<ManufacturingScrapCostPolicyViewV1> GetScrapCostAsync(CancellationToken cancellationToken = default);
    Task<CompanyPolicyCommandResultV1> RecordScrapCostAsync(RecordManufacturingScrapCostRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<NotificationRetentionPolicyViewV1> GetNotificationRetentionAsync(CancellationToken cancellationToken = default);
    Task<CompanyPolicyCommandResultV1> RecordNotificationRetentionAsync(RecordNotificationRetentionRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<WorkApprovalDurationPolicyViewV1> GetWorkApprovalDurationAsync(CancellationToken cancellationToken = default);
    Task<CompanyPolicyCommandResultV1> RecordWorkApprovalDurationHoursAsync(RecordWorkApprovalDurationRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<CompanyPolicyCommandResultV1> RecordWorkApprovalDurationBusinessDaysAsync(RecordWorkApprovalDurationRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
