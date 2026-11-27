using System.Text.Json.Serialization;
namespace GoymenERP.Modules.Accounting.Application;

public interface IAccountingInventoryCostCancellationDecisionReadV1
{
    Task<AccountingApiResult<AccountingInventoryCostCancellationDecisionDetailV1>> ReadCancellationDecisionAsync(Guid proposalId,
        long? preparedCancellationVersion, Func<CancellationToken, Task> freshConsumerFence, CancellationToken token = default);
}
public interface IAccountingInventoryCostCancellationDecisionStoreV1
{
    Task<AccountingInventoryCostCancellationDecisionDetailV1?> ReadCancellationDecisionAsync(Guid proposalId,
        long? preparedCancellationVersion, Func<CancellationToken, Task> freshReadFence, CancellationToken token = default);
}
/// <summary>Retained N and prepared M are independent; no current account plan or replacement preparation.</summary>
public sealed record AccountingInventoryCostCancellationDecisionDetailV1(AccountingInventoryCostProposalV1 OriginalProposal,
    InventoryCostCancellationV1 PreparedCancellation, InventoryCostCancellationV1 Cancellation,
    DateTimeOffset PreparedRecordedAtUtc, AccountingInventoryCostCancellationSourceV1 SourceCancellation,
    string SourceFactsDigest, string PreviewDigest)
{
    [JsonIgnore] public bool? Pending { get; init; }
}
public sealed record AccountingInventoryCostCancellationSourceV1(Guid CompanyId, Guid CancellationId, string DocumentKind,
    Guid SourceDocumentId, Guid InvoiceId, long FinalizedVersion, Guid CancelledBy, DateTimeOffset CancelledAtUtc,
    string Reason, string RequestHash, string CorrelationId, DateTimeOffset RecordedAtUtc);
public sealed record AccountingInventoryCostCancellationApprovalReceiptV1(Guid CompanyId, Guid InventoryCostProposalId,
    Guid ComponentCancellationId, Guid SourceCancellationId, string CommandName, string IdempotencyKeyHash,
    string PayloadHash, Guid ActorId, long ExpectedProposalVersion, long ExpectedCancellationVersion,
    long ResultProposalVersion, long ResultCancellationVersion, Guid PreparedBy, DateTimeOffset PreparedAtUtc,
    DateTimeOffset PreparedRecordedAtUtc, DateTimeOffset DecidedAtUtc, string OriginalCorrelationId,
    string SourceFactsDigest, string OriginalResultDigest);
