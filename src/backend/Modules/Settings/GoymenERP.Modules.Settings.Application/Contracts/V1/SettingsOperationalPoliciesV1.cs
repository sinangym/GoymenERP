namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

public static class SettingsOperationalPolicyPermissionsV1
{
    // Technical registration only. Real grant/role mapping remains human-owned.
    public const string ScrapCostManage = "settings.manufacturing-scrap-cost.manage";
    public const string NotificationRetentionManage = "settings.notification-retention.manage";
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
}
