using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Inventory-owned R20 contract; central host routing and permission registration are separate contributions.</summary>
public static class InventoryTransferApiV1
{
    public const string Path = "/api/v1/inventory/transfers";
    public const string DispatchPathTemplate = "/api/v1/inventory/transfers/{transferId}/dispatch";
    public const string AcceptPathTemplate = "/api/v1/inventory/transfers/{transferId}/accept-lines/{lineId}";
    public const int MaximumLines = 100;

    /// <summary>
    /// Canonical positive document-version text: ASCII digits, no sign, no leading zero, within the Logistics int range.
    /// </summary>
    public static bool TryParseOutgoingDocumentVersion(string? value, out int version)
    {
        version = 0;
        return !string.IsNullOrEmpty(value) && value[0] != '0' && value.All(char.IsAsciiDigit)
            && int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out version)
            && version > 0;
    }
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

/// <summary>
/// B-22a dispatch request: the Logistics-owned outgoing document is named only by its identity and the exact version the
/// caller saw (canonical positive integer text). No free-form document kind can select the owner; Inventory asks the
/// typed <see cref="IInventoryTransferOutgoingDocumentProofPortV1"/> and moves stock only on a current effective proof.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DispatchInventoryTransferV1(
    [property: JsonRequired] string ExpectedTransferVersion,
    [property: JsonRequired] DateTimeOffset DispatchedAtUtc,
    [property: JsonRequired] Guid OutgoingDocumentId,
    [property: JsonRequired] string ExpectedOutgoingDocumentVersion);

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
    Task<InventoryTransferResultV1?> ReplayPlanAsync(
        PlanInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferResultV1> PlanAsync(
        PlanInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored answer for this dispatch key — the dispatched transfer, or a recorded terminal refusal re-thrown as a
    /// replayed <see cref="InventoryTransferException"/> — or null when the key is new.
    /// </summary>
    Task<InventoryTransferResultV1?> ReplayDispatchAsync(
        Guid transferId,
        DispatchInventoryTransferV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches only against the exact current effective outgoing-document binding the proof port returned; the store
    /// re-checks it against the stored company, transfer, requested document version and the stored ordered line set.
    /// </summary>
    Task<InventoryTransferResultV1> DispatchAsync(
        Guid transferId,
        DispatchInventoryTransferV1 request,
        InventoryTransferOutgoingDocumentOwnerSnapshotV1 effectiveOutgoingDocument,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a terminal dispatch refusal (a 409 outgoing-document state or version refusal) under the dispatch key with
    /// its audit entry, so the same key replays the same refusal. No stock, movement or transfer row changes. Returns null
    /// when the refusal was recorded, or the answer the key already holds (a recorded refusal re-throws itself).
    /// </summary>
    Task<InventoryTransferResultV1?> RecordDispatchRefusalAsync(
        Guid transferId,
        DispatchInventoryTransferV1 request,
        InventoryTransferException refusal,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferResultV1?> ReplayAcceptLineAsync(
        Guid transferId,
        Guid lineId,
        AcceptInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default);

    Task<InventoryTransferResultV1> AcceptLineAsync(
        Guid transferId,
        Guid lineId,
        AcceptInventoryTransferLineV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
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

    // B-22a outgoing-document proof refusals (K2: only a final/effective Logistics document opens dispatch).
    public const string OutgoingDocumentNotFound = "inventory_transfer_outgoing_document_not_found";
    public const string OutgoingDocumentBindingMismatch = "inventory_transfer_outgoing_document_binding_mismatch";
    public const string OutgoingDocumentNotFinal = "inventory_transfer_outgoing_document_not_final";
    public const string OutgoingDocumentCancelled = "inventory_transfer_outgoing_document_cancelled";
    public const string OutgoingDocumentReversed = "inventory_transfer_outgoing_document_reversed";
    public const string OutgoingDocumentVersionConflict = "inventory_transfer_outgoing_document_version_conflict";
}

/// <summary>The fixed owner kind stored on a B-22a dispatch row; it is never read from the request.</summary>
public static class InventoryTransferDocumentKindsV1
{
    public const string LogisticsOutgoingDocument = "logistics.transfer-outgoing-document";
}

public sealed class InventoryTransferException(int status, string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public InventoryTransferException(int status, string code, bool isReplay, string originalCorrelationId)
        : this(status, code)
    {
        IsReplay = isReplay;
        OriginalCorrelationId = originalCorrelationId;
    }

    public int Status { get; } = status;
    public string Code { get; } = code;

    /// <summary>True when this is a recorded terminal refusal answered again for the same idempotency key.</summary>
    public bool IsReplay { get; }

    /// <summary>The correlation of the request that recorded the refusal; set only on a replay.</summary>
    public string? OriginalCorrelationId { get; }
}
