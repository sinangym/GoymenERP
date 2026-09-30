using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Inventory-owned R20 contract; central host routing and permission registration are separate contributions.</summary>
public static class InventoryTransferApiV1
{
    public const string Path = "/api/v1/inventory/transfers";
    public const string DispatchPathTemplate = "/api/v1/inventory/transfers/{transferId}/dispatch";
    public const string AcceptPathTemplate = "/api/v1/inventory/transfers/{transferId}/accept-lines/{lineId}";
    public const int MaximumLines = 100;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryTransferDocumentReferenceV1(
    [property: JsonRequired] string Kind,
    [property: JsonRequired] Guid Id,
    Guid? LineId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlanInventoryTransferLineV1(
    [property: JsonRequired] Guid LineId,
    [property: JsonRequired] Guid LabelId,
    [property: JsonRequired] string ExpectedLabelVersion,
    [property: JsonRequired] string PlannedKg);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlanInventoryTransferV1(
    [property: JsonRequired] Guid TransferId,
    [property: JsonRequired] Guid SourceLocationId,
    [property: JsonRequired] string SourceCustodianKind,
    [property: JsonRequired] Guid SourceCustodianId,
    [property: JsonRequired] Guid DestinationLocationId,
    [property: JsonRequired] string DestinationCustodianKind,
    [property: JsonRequired] Guid DestinationCustodianId,
    [property: JsonRequired] DateTimeOffset PlannedAtUtc,
    [property: JsonRequired] IReadOnlyList<PlanInventoryTransferLineV1> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DispatchInventoryTransferV1(
    [property: JsonRequired] string ExpectedTransferVersion,
    [property: JsonRequired] DateTimeOffset DispatchedAtUtc,
    [property: JsonRequired] InventoryTransferDocumentReferenceV1 OutgoingDocument);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcceptInventoryTransferLineV1(
    [property: JsonRequired] string ExpectedTransferVersion,
    [property: JsonRequired] DateTimeOffset AcceptedAtUtc,
    [property: JsonRequired] InventoryTransferDocumentReferenceV1 IncomingDocument);

public sealed record InventoryTransferLineViewV1(
    Guid LineId,
    Guid LabelId,
    string ExpectedLabelVersion,
    string PlannedKg,
    string? ActualShippedKg,
    string? DifferenceKg,
    string Status);

public sealed record InventoryTransferViewV1(
    Guid TransferId,
    Guid CompanyId,
    string Version,
    string Status,
    Guid SourceLocationId,
    string SourceCustodianKind,
    Guid SourceCustodianId,
    Guid DestinationLocationId,
    string DestinationCustodianKind,
    Guid DestinationCustodianId,
    DateTimeOffset PlannedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    IReadOnlyList<InventoryTransferLineViewV1> Lines);

public sealed record InventoryTransferResultV1(
    InventoryTransferViewV1 Transfer,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>
/// Inventory's transactional write boundary. The central command adapter supplies a freshly authorized company/actor
/// envelope; no permission name, role, grant, route publication or negative-stock override is introduced here.
/// </summary>
public interface IInventoryTransferWriterV1
{
    Task<InventoryTransferResultV1> PlanAsync(
        PlanInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferResultV1> DispatchAsync(
        Guid transferId,
        DispatchInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferResultV1> AcceptLineAsync(
        Guid transferId,
        Guid lineId,
        AcceptInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTransferReaderV1
{
    Task<InventoryTransferViewV1?> ReadAsync(Guid transferId, CancellationToken cancellationToken = default);
}

public interface IInventoryTransferCommandV1
{
    Task<InventoryTransferResultV1> PlanAsync(
        PlanInventoryTransferV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferResultV1> DispatchAsync(
        Guid transferId,
        DispatchInventoryTransferV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferResultV1> AcceptLineAsync(
        Guid transferId,
        Guid lineId,
        AcceptInventoryTransferLineV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTransferQueryV1
{
    Task<InventoryTransferViewV1> GetAsync(Guid transferId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The destination context as its owning modules see it now. Inventory passes the transaction company, physical
/// location and typed custodian; the central adapter validates existence, active state, company isolation and the
/// location/custodian pairing without granting the caller an Organization or Counterparty read.
/// </summary>
public enum InventoryTransferDestinationStatusV1
{
    Active = 1,
    NotFound = 2,
    Inactive = 3,
    ContextMismatch = 4,
}

public interface IInventoryTransferDestinationPortV1
{
    Task<InventoryTransferDestinationStatusV1> CheckAsync(
        Guid companyId,
        Guid locationId,
        string custodianKind,
        Guid custodianId,
        CancellationToken cancellationToken = default);
}

public static class InventoryTransferCodesV1
{
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string PermissionUnregistered = "inventory_permission_unregistered";
    public const string ValidationFailed = "validation_failed";
    public const string TransferNotFound = "inventory_transfer_not_found";
    public const string TransferVersionConflict = "inventory_transfer_version_conflict";
    public const string TransferStateConflict = "inventory_transfer_state_conflict";
    public const string LabelNotFound = "inventory_label_not_found";
    public const string LabelVersionConflict = "inventory_label_version_conflict";
    public const string SourcePositionConflict = "inventory_transfer_source_position_conflict";
    public const string ActiveAllocationConflict = "inventory_transfer_active_allocation_conflict";
    public const string WholeLabelRequired = "inventory_transfer_whole_label_required";
    public const string DestinationNotFound = "inventory_transfer_destination_not_found";
    public const string DestinationInactive = "inventory_transfer_destination_inactive";
    public const string DestinationContextMismatch = "inventory_transfer_destination_context_mismatch";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string ConcurrentChange = "inventory_concurrent_change";
    public const string Unavailable = "inventory_transfer_unavailable";
}

public sealed class InventoryTransferException(int status, string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
