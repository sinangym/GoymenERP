using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>B-04b: where an admission stands. Waiting is Accounting's own configuration gap ("eşleme bekliyor"); Admitted and Refused are final for the key.</summary>
public enum AccountingSourceDocumentAdmissionStatusV1
{
    Waiting = 1,
    Admitted = 2,
    Refused = 3,
}

/// <summary>Store-level codes of the admission store (not an HTTP surface).</summary>
public static class AccountingSourceDocumentStoreCodesV1
{
    /// <summary>The stored admission of the key holds other document content. Nothing was stored.</summary>
    public const string ContentConflict = "accounting_source_document_content_conflict";
}

/// <summary>The stored admission of one source document (the attempt record and, once final, the terminal refusal record).</summary>
public sealed record AccountingSourceDocumentAdmissionRecordV1(
    Guid SourceDocumentId,
    string DocumentKind,
    Guid InvoiceId,
    long FinalizedVersion,
    string IdempotencyKeyHash,
    string RequestHash,
    AccountingSourceDocumentAdmissionStatusV1 Status,
    string ReasonCode,
    Guid? VoucherDraftId,
    int AttemptCount,
    long Version,
    string CorrelationId);

/// <summary>One draft voucher line. Side 1 = debit, 2 = credit; the amount is positive and in the functional currency.</summary>
public sealed record AccountingSourceDocumentVoucherLineV1(
    int LineNumber,
    string MappingKey,
    int Side,
    Guid AccountId,
    decimal Amount,
    int? SourceLineNumber,
    string? VatRateCode);

/// <summary>The draft voucher Accounting derived; <paramref name="MappingVersion"/> is the one mapping version every account came from.</summary>
public sealed record AccountingSourceDocumentVoucherDraftV1(
    Guid DraftId,
    long MappingVersion,
    IReadOnlyList<AccountingSourceDocumentVoucherLineV1> Lines);

/// <summary>
/// One write of the admission store: ONE transaction, ONE save of the admission row (insert at
/// <paramref name="ExpectedVersion"/> 0, else update of exactly that version), the optional draft with its lines and the audit event.
/// </summary>
public sealed record AccountingSourceDocumentAdmissionWriteV1(
    AccountingCommandMetadata Metadata,
    AccountingSourceDocumentAdmissionRequestV1 Request,
    string RequestHash,
    string KeyHash,
    AccountingSourceDocumentAdmissionStatusV1 Status,
    string ReasonCode,
    long ExpectedVersion,
    AccountingSourceDocumentVoucherDraftV1? Draft);

public interface IAccountingSourceDocumentAdmissionStoreV1
{
    /// <summary>The admission of the server-resolved company found by key hash, else by (kind, invoice, finalized version).</summary>
    Task<AccountingSourceDocumentAdmissionRecordV1?> FindAdmissionAsync(
        string keyHash,
        string documentKind,
        Guid invoiceId,
        long finalizedVersion,
        CancellationToken cancellationToken = default);

    /// <exception cref="AccountingStoreException"><see cref="AccountingStoreCodes.ConcurrentChange"/>,
    /// <see cref="AccountingStoreCodes.CompanyContextMismatch"/> or <see cref="AccountingSourceDocumentStoreCodesV1.ContentConflict"/>.</exception>
    Task<AccountingSourceDocumentAdmissionRecordV1> WriteAsync(
        AccountingSourceDocumentAdmissionWriteV1 write,
        CancellationToken cancellationToken = default);
}
