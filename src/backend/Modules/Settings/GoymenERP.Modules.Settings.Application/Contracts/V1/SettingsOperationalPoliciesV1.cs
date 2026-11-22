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
/// YOL-D18 (W1-A, WP1 D3): the lengths of the two approval duration classes. Until the company records a version, the decided
/// YOL-D18 values apply as version 0 (<see cref="SettingsWorkApprovalDurationLimitsV1.DecidedDefaultHours"/>,
/// <see cref="SettingsWorkApprovalDurationLimitsV1.DecidedDefaultBusinessDays"/>); a recorded version replaces them. A null
/// length is reserved for a reader that cannot name a length: it shows "süre tanımlı değil" and assumes nothing. Each length
/// carries its own version text.
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
    /// <summary>YOL-D18 (D3): the decided hour-scale length (4 hours), in force as version 0 until a version is recorded.</summary>
    public const int DecidedDefaultHours = 4;
    /// <summary>YOL-D18 / 125 (D3): the decided business-day length (2 business days), in force as version 0 until recorded.</summary>
    public const int DecidedDefaultBusinessDays = 2;
}
/// <summary>YOL-D22: a listed day is an official holiday or an eve (arife); both are deducted from business days.</summary>
public enum WorkHolidayKindV1
{
    Holiday = 1,
    Eve = 2,
}
/// <summary>YOL-D22: the latest recorded holiday list of one year; a year missing from the answer is "not defined".</summary>
public sealed record SettingsWorkHolidayCalendarYearV1(int Year, long Version, IReadOnlyList<DateOnly> Days)
{
    public string VersionText => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"work.holiday-calendar.{Year}:{Version}");
}
public sealed record SettingsWorkHolidayCalendarsV1(Guid CompanyId, IReadOnlyList<SettingsWorkHolidayCalendarYearV1> DefinedYears);
public interface ISettingsWorkHolidayCalendarProviderV1
{
    /// <summary>The defined years among <paramref name="fromYear"/>..<paramref name="toYear"/> (inclusive) with their latest list.</summary>
    Task<SettingsWorkHolidayCalendarsV1> GetWorkHolidayCalendarsAsync(Guid companyId, int fromYear, int toYear,
        CancellationToken cancellationToken = default);
}
public static class SettingsWorkHolidayCalendarLimitsV1
{
    public const int MinimumYear = 2000;
    public const int MaximumYear = 2100;
    /// <summary>Input bound only: no year lists more official holidays and eves than this.</summary>
    public const int MaximumDays = 60;
}
public sealed record RecordWorkHolidayDayV1(DateOnly? Date, string? Kind);
public sealed record RecordWorkHolidayCalendarRequestV1(string? ExpectedVersion, IReadOnlyList<RecordWorkHolidayDayV1>? Days, string? Reason);
public sealed record WorkHolidayCalendarDayViewV1(DateOnly Date, string Kind);
public sealed record WorkHolidayCalendarVersionViewV1(string Version, int DayCount, string RecordedBy, DateTimeOffset RecordedAtUtc,
    string Reason);
public sealed record WorkHolidayCalendarViewV1(Guid CompanyId, int Year, bool IsDefined, string Version,
    IReadOnlyList<WorkHolidayCalendarDayViewV1> Days, IReadOnlyList<WorkHolidayCalendarVersionViewV1> Versions);
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
    Task<WorkHolidayCalendarViewV1> GetWorkHolidayCalendarAsync(int year, CancellationToken cancellationToken = default);
    Task<CompanyPolicyCommandResultV1> RecordWorkHolidayCalendarAsync(int year, RecordWorkHolidayCalendarRequestV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
