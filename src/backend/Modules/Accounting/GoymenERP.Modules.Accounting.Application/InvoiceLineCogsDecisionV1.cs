namespace GoymenERP.Modules.Accounting.Application;

public interface IInvoiceLineCogsDecisionReadV1
{
    Task<AccountingApiResult<InvoiceLineCogsDecisionDetailV1>> ReadDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record InvoiceLineCogsDecisionDetailV1(InvoiceLineCogsCorrectionViewV1 Proposal,
    FinalizedInvoiceLineReferenceV1 Source, long CurrentLineVersion, string SourceFactsDigest, string PreviewDigest);
public sealed record InvoiceLineCogsDecisionSourceV1(InvoiceLineCogsCorrectionViewV1 Prepared,
    InvoiceLineCogsCorrectionViewV1 Proposal, FinalizedInvoiceLineReferenceV1 Source, string SourceHash,
    long CurrentLineVersion, bool RevisionBound);
public interface IInvoiceLineCogsDecisionStoreV1
{
    Task<InvoiceLineCogsDecisionSourceV1?> ReadCogsDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
}
public sealed record InvoiceLineCogsApprovalReceiptV1(Guid CompanyId, Guid ProposalId, Guid InvoiceId, Guid InvoiceLineId,
    Guid PreparedBy, DateTimeOffset PreparedAtUtc, string CommandName, string IdempotencyKeyHash, string PayloadHash,
    Guid ActorId, long ExpectedVersion, long ResultVersion, string OriginalCorrelationId, DateTimeOffset ApprovedAtUtc,
    string SourceFactsDigest, string OriginalResultDigest);
