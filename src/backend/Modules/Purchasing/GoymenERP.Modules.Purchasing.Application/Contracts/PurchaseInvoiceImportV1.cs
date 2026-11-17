namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed record PurchaseInvoiceImportReservationV1(Guid Id, Guid CompanyId, string Version,
    string ReservedBy, DateTimeOffset ReservedAtUtc);
public sealed record PurchaseInvoiceImportReservationResultV1(PurchaseInvoiceImportReservationV1 Reservation,
    bool IsReplay, string OriginalCorrelationId);
public sealed record PurchaseInvoiceImportReservationPageV1(Guid CompanyId, int PageIndex, bool HasNext,
    IReadOnlyList<PurchaseInvoiceImportReservationV1> Items);
public sealed record PurchaseInvoiceImportPreviewRequestV1(string ExpectedVersion, Guid DocumentId, int DocumentVersion,
    string DocumentSha256, PurchaseInvoiceFileReadOptionsV1 Options);
public sealed record PurchaseInvoiceImportPreviewV1(Guid ImportId, Guid CompanyId, string Version, Guid DocumentId,
    int DocumentVersion, string DocumentSha256, PurchaseInvoiceFileFactsV1? Invoice, IReadOnlyList<PurchaseInvoiceFileIssueV1> Issues);

/// <summary>Only the reserved source identity and a read preview. No matching, invoice finalization or Finance event.</summary>
public interface IPurchaseInvoiceImportV1
{
    Task<PurchaseInvoiceImportReservationPageV1> ListAsync(int pageIndex, CancellationToken token = default);
    Task<PurchaseInvoiceImportReservationResultV1> ReserveAsync(PurchasingCommandMetadata metadata, CancellationToken token = default);
    Task<PurchaseInvoiceImportReservationV1> ReadAsync(Guid importId, CancellationToken token = default);
    Task<PurchaseInvoiceImportPreviewV1> PreviewAsync(Guid importId, PurchaseInvoiceImportPreviewRequestV1 request, CancellationToken token = default);
}

/// <summary>Purchasing owns the reservation; Documents never opens Purchasing tables.</summary>
public interface IPurchaseInvoiceImportReservationStoreV1
{
    Task<PurchaseInvoiceImportReservationPageV1> ListAsync(int pageIndex, CancellationToken token = default);
    Task<PurchaseInvoiceImportReservationV1?> FindAsync(Guid id, CancellationToken token = default);
    Task<PurchaseInvoiceImportReservationResultV1> ReserveAsync(PurchasingWriteEnvelope envelope,
        Func<CancellationToken, Task> requireStillAuthorized, CancellationToken token = default);
}

/// <summary>Host reads one clean, hash-verified immutable version through the actual Documents owner,
/// and proves that version belongs to this reservation before returning bytes.</summary>
public interface IPurchaseInvoiceImportDocumentReadV1
{
    Task<PurchaseInvoiceImportDocumentV1> ReadAsync(Guid importId, Guid documentId, int version, CancellationToken token = default);
}
public sealed record PurchaseInvoiceImportDocumentV1(Guid CompanyId, Guid DocumentId, int Version, string Sha256,
    PurchaseInvoiceFileFormatV1 Format, byte[] Content);
