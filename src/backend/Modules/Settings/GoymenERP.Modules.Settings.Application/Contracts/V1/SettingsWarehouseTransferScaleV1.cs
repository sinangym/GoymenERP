using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>LOJ-D11, KLN-D25(7)/D27: technical registration only; no assignment or activation.</summary>
public static class SettingsWarehouseTransferScaleV1
{
    public const string Manage = "settings.warehouse-transfer-scale.manage";
    public const string SourceVersion = "settings.warehouse-transfer-scale.v1";
    public const int MaximumReasonLength = 4000;
}

public enum SettingsWarehouseTransferScaleStatusV1 { NotConfigured = 1, Configured = 2, Unavailable = 3 }

/// <summary>
/// A virgin warehouse is NotConfigured with the decided Off value and no invented recorded version. An explicit Off
/// version is Configured. A failed read is Unavailable with neither value nor version. This does not enable any flow.
/// </summary>
public sealed record SettingsWarehouseTransferScalePolicyV1(SettingsWarehouseTransferScaleStatusV1 Status,
    Guid CompanyId, Guid LocationId, bool? RequireExitWeighing, long? RecordedVersion, string SourceVersion);

public interface ISettingsWarehouseTransferScaleProviderV1
{
    Task<SettingsWarehouseTransferScalePolicyV1> GetAsync(Guid companyId, Guid locationId,
        CancellationToken cancellationToken = default);
}

public sealed record SettingsTransferWarehouseV1(Guid CompanyId, Guid LocationId, bool IsActive, bool IsExternal,
    long LocationVersion);

/// <summary>Host bridge to the actual Organization owner; it does not require a new Organization user-read grant.</summary>
public interface ISettingsTransferWarehouseSourceV1
{
    Task<SettingsTransferWarehouseV1?> ReadAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// LOJ-D11: On may be recorded only while the actual Logistics exit reader/fence is supported for this warehouse.
/// A registration, configuration flag or mock is not a positive owner answer. This read neither seeds nor activates.
/// </summary>
public interface ISettingsTransferDepartureCapabilityV1
{
    Task<bool> IsSupportedAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken = default);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordWarehouseTransferScaleV1(string? ExpectedVersion, bool? RequireExitWeighing, string? Reason);
public sealed record WarehouseTransferScaleVersionV1(Guid RecordId, string Version, bool RequireExitWeighing,
    long LocationVersion, string RecordedBy, DateTimeOffset RecordedAtUtc, string Reason);
public sealed record WarehouseTransferScaleViewV1(Guid CompanyId, Guid LocationId,
    SettingsWarehouseTransferScalePolicyV1 Policy, IReadOnlyList<WarehouseTransferScaleVersionV1> Versions);

public interface ISettingsWarehouseTransferScaleAdministrationV1
{
    Task<WarehouseTransferScaleViewV1> ReadAsync(Guid locationId, CancellationToken cancellationToken = default);
    Task<CompanyPolicyCommandResultV1> RecordAsync(Guid locationId, RecordWarehouseTransferScaleV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}
