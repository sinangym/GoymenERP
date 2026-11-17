using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>14 October transfer decisions 1/2: the Inventory target acceptance document has its own identity and version.</summary>
public static class InventoryTransferReceiptDocumentV1
{
    public const string Kind = "inventory.transfer.incoming";
    public const string Path = "/api/v1/inventory/transfer-receipt-documents";
    public const string SourceVersion = "inventory.transfer-receipt-document.v1";
    public const string Invalid = "inventory_transfer_receipt_document_invalid";
    public const string NotFound = "inventory_transfer_receipt_document_not_found";
    public const string NotEffective = "inventory_transfer_receipt_document_not_effective";
    public const string VersionConflict = "inventory_transfer_receipt_document_version_conflict";
    public const string BindingConflict = "inventory_transfer_receipt_document_binding_conflict";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateInventoryTransferReceiptDocumentV1(Guid DocumentId, Guid TransferId,
    string ExpectedTransferVersion, DateTimeOffset ReceivedAtUtc, IReadOnlyList<InventoryTransferReceiptDocumentLineInputV1> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryTransferReceiptDocumentLineInputV1(Guid DocumentLineId, Guid TransferLineId, string ReceivedKg,
    string? OverageReason = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinalizeInventoryTransferReceiptDocumentV1(string ExpectedDocumentVersion,
    string ExpectedTransferVersion, string Reason);

public sealed record InventoryTransferReceiptDocumentLineV1(Guid DocumentLineId, Guid TransferLineId, Guid LabelId,
    string DispatchedKg, string ReceivedKg, string? OverageReason);

public sealed record InventoryTransferReceiptDocumentViewV1(Guid CompanyId, Guid DocumentId, Guid TransferId,
    string Version, string Status, string SourceTransferVersion, DateTimeOffset ReceivedAtUtc,
    IReadOnlyList<InventoryTransferReceiptDocumentLineV1> Lines, string CreatedBy, DateTimeOffset CreatedAtUtc,
    string? FinalizedBy, DateTimeOffset? FinalizedAtUtc, string? FinalizationReason, string SourceVersion);

public sealed record InventoryTransferReceiptDocumentsV1(Guid CompanyId, Guid TransferId,
    IReadOnlyList<InventoryTransferReceiptDocumentViewV1> Items, bool SearchWindowFull, string? NextCursor = null);
public sealed record InventoryTransferReceiptDocumentResultV1(InventoryTransferReceiptDocumentViewV1 Document,
    bool IsReplay, string OriginalCorrelationId);

public interface IInventoryTransferReceiptDocumentsV1
{
    Task<InventoryTransferReceiptDocumentsV1> ListAsync(Guid transferId, string? cursor = null, CancellationToken cancellationToken = default);
    Task<InventoryTransferReceiptDocumentResultV1> CreateAsync(CreateInventoryTransferReceiptDocumentV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<InventoryTransferReceiptDocumentResultV1> FinalizeAsync(Guid documentId,
        FinalizeInventoryTransferReceiptDocumentV1 request, InventoryCommandMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>Narrow module cursor binding. The host's existing Inventory signing configuration is used, never a generated fallback key.</summary>
public interface IInventoryTransferReceiptDocumentCursorV1
{
    bool IsAvailable { get; }
    string Protect(Guid companyId, string actorId, Guid transferId, Guid afterDocumentId);
    Guid Unprotect(Guid companyId, string actorId, Guid transferId, string cursor);
}
