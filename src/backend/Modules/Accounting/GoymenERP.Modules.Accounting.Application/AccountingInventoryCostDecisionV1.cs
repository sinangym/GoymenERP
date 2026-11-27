using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>One exact retained cost proposal under its own approval permission; no general browse grant.</summary>
public interface IAccountingInventoryCostDecisionReadV1
{
    Task<AccountingApiResult<AccountingInventoryCostDecisionDetailV1>> ReadDecisionAsync(Guid proposalId,
        AccountingInventoryCostKindV1 kind, Func<CancellationToken, Task> freshConsumerFence, CancellationToken token = default);
}
public sealed record AccountingInventoryCostDecisionDetailV1(AccountingInventoryCostProposalV1 Proposal,
    DateTimeOffset PreparedRecordedAtUtc, string SourceFactsDigest, string PreviewDigest)
{
    // Current cancellation controls pending eligibility, never the immutable original answer.
    [JsonIgnore] public bool? SourceCancelled { get; init; }
}
public sealed record AccountingInventoryCostDecisionSourceV1(AccountingInventoryCostProposalV1 Prepared,
    AccountingInventoryCostProposalV1 Proposal, DateTimeOffset PreparedRecordedAtUtc,
    string SourceJson, string SourceHash, bool SourceCancelled, bool JournalBound, string? JournalCorrelationId);
public interface IAccountingInventoryCostDecisionStoreV1
{
    // null selects the current retained proposal; a value selects ONLY that original result version.
    Task<AccountingInventoryCostDecisionSourceV1?> ReadInventoryCostDecisionAsync(Guid proposalId,
        AccountingInventoryCostKindV1 kind, long? resultVersion, Func<CancellationToken, Task> freshReadFence, CancellationToken token = default);
}
public sealed record AccountingInventoryCostApprovalReceiptV1(Guid CompanyId, Guid ProposalId,
    AccountingInventoryCostKindV1 Kind, Guid EventId, Guid InvoiceId, Guid PreparedBy,
    DateTimeOffset PreparedAtUtc, DateTimeOffset PreparedRecordedAtUtc, string CommandName,
    string IdempotencyKeyHash, string PayloadHash, Guid ActorId, long ExpectedVersion, long ResultVersion,
    string ResultStatus, string OriginalCorrelationId, DateTimeOffset DecidedAtUtc,
    string SourceFactsDigest, string OriginalResultDigest);
