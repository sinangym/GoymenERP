namespace GoymenERP.Modules.Accounting.Application;

public interface IAccountingSourceVoucherDecisionReadV1
{
    Task<AccountingApiResult<AccountingSourceVoucherDecisionDetailV1>> ReadDecisionAsync(Guid voucherId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public interface IAccountingSourceVoucherDecisionStoreV1
{
    Task<AccountingSourceVoucherDecisionSourceV1?> ReadSourceVoucherDecisionAsync(Guid voucherId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
}
public sealed record AccountingSourceVoucherDecisionDetailV1(AccountingSourceVoucherViewV1 Voucher,
    DateTimeOffset CreatedAtUtc, bool SourceCancelled, bool PeriodOpen, string SourceFactsDigest, string PreviewDigest);
// These are actual stored commercial fields, including totals omitted from the native human DTO. Never a draft history.
public sealed record AccountingSourceVoucherCommercialAmountsV1(decimal DocumentNet, decimal DocumentVat,
    decimal DocumentWithholding, decimal DocumentPayable, decimal FunctionalNet, decimal FunctionalVat,
    decimal FunctionalWithholding, decimal FunctionalPayable);
public sealed record AccountingSourceVoucherDecisionSourceV1(AccountingSourceVoucherViewV1 StoredVoucher,
    AccountingSourceVoucherViewV1 CurrentVoucher, DateTimeOffset CreatedAtUtc,
    AccountingSourceVoucherCommercialAmountsV1 CommercialAmounts, bool SourceCancelled, bool PeriodOpen,
    bool OriginalJournalBound, string? OriginalJournalCorrelationId);
public sealed record AccountingSourceVoucherApprovalReceiptV1(Guid CompanyId, Guid VoucherId,
    Guid SourceDocumentId, Guid InvoiceId, Guid SourceActorId, DateTimeOffset CreatedAtUtc,
    string CommandName, string IdempotencyKeyHash, string PayloadHash, Guid ActorId,
    long ExpectedVersion, long ResultVersion, Guid JournalId, string OriginalCorrelationId,
    DateTimeOffset ApprovedAtUtc, string SourceFactsDigest, string OriginalResultDigest);
