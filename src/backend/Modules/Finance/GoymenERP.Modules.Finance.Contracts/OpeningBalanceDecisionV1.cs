using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// Only the native opening view (including the specifically requested reversal) is human JSON.
public sealed record OpeningBalanceDecisionDetailV1(OpeningBalanceView OpeningBalance,
    [property: JsonIgnore] OpeningBalanceDecisionProofV1? DecisionProof = null);

public sealed record OpeningBalanceDecisionProofV1(Guid CompanyId, Guid OpeningBalanceId, Guid? ReversalId,
    string OpeningProposedBy, string ProposedBy, DateTimeOffset ProposedAtUtc, string Status, string Version,
    string OpeningFactsDigest, string? ReversalFactsDigest, string PreviewDigest);

public sealed record OpeningBalanceDecisionReceiptV1(Guid CompanyId, Guid OpeningBalanceId, Guid? ReversalId,
    string CommandName, string IdempotencyKeyHash, string PayloadHash, string ActorId, string ExpectedVersion,
    string ResultVersion, string OriginalCorrelationId, DateTimeOffset RecordedAtUtc, string OpeningFactsDigest,
    string? ReversalFactsDigest, string OriginalReceiptDigest);

public interface IFinanceOpeningBalanceDecisionReadV1
{
    Task<ReadResult<OpeningBalanceDecisionDetailV1>> ReadOpeningDecisionAsync(FinanceContext context, Guid openingId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken);
    Task<ReadResult<OpeningBalanceDecisionDetailV1>> ReadReversalDecisionAsync(FinanceContext context, Guid reversalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken);
}

// The Work route identifies a retained reversal, while the unchanged public native command identifies its parent.
public interface IFinanceOpeningBalanceReversalDecisionV1
{
    Task<OpeningBalanceCommandResult> ApproveReversalDecisionAsync(FinanceContext context, CommandMetadata metadata,
        Guid reversalId, OpeningBalanceReversalApproveRequest request, CancellationToken cancellationToken);
    Task<OpeningBalanceCommandResult> RejectReversalDecisionAsync(FinanceContext context, CommandMetadata metadata,
        Guid reversalId, OpeningBalanceReversalRejectRequest request, CancellationToken cancellationToken);
}
