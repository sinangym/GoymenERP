using System.Text.Json.Serialization;
using GoymenERP.Modules.Accounting.Domain;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Purpose-only approval disclosure; no general journal or amount read permission is granted.</summary>
public interface IAccountingJournalReversalDecisionReadV1
{
    Task<AccountingApiResult<AccountingJournalReversalDecisionDetailV1>> ReadDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record AccountingJournalReversalDecisionDetailV1(AccountingJournalReversalProposalViewV1 Proposal,
    string SourceFactsDigest, string PreviewDigest)
{
    [JsonIgnore] public Guid? CancellationSourceActorId { get; init; }
}
public sealed record AccountingJournalReversalApprovalReceiptV1(Guid CompanyId, Guid ProposalId, Guid SourceJournalId,
    Guid ReversalJournalId, Guid PeriodId, string CommandName, string IdempotencyKeyHash, string PayloadHash,
    Guid ActorId, int ExpectedVersion, int ResultVersion, string OriginalCorrelationId, DateTimeOffset ApprovedAtUtc,
    string SourceFactsDigest, string OriginalResultDigest);
public sealed record AccountingJournalReversalDecisionSourceV1(AccountingJournalReversalProposalV1 Proposal,
    bool SourceJournalExists, bool ReversalJournalBound, string? ReversalCorrelationId, Guid? CancellationSourceActorId);
public interface IAccountingJournalReversalDecisionStoreV1
{
    Task<AccountingJournalReversalDecisionSourceV1?> ReadJournalReversalDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
}
