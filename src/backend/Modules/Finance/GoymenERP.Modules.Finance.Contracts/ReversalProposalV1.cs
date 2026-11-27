using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

public sealed record ReversalProposalCommandResult(
    CommandOutcome Outcome,
    ReversalProposalView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues)
{
    // Server-only native command provenance; the existing public five-field result is unchanged.
    [JsonIgnore]
    public ReceiptReversalDecisionReceiptV1? DecisionReceipt { get; init; }
}

public sealed record ReversalProposalPage(IReadOnlyList<ReversalProposalView> Items);

public interface IReversalProposalService
{
    Task<ReversalProposalCommandResult> ProposeAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ProposeReversalRequest request,
        CancellationToken cancellationToken);

    Task<ReversalProposalCommandResult> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ApproveReversalRequest request,
        CancellationToken cancellationToken);

    Task<ReversalProposalCommandResult> RejectAsync(
        FinanceContext context,
        CommandMetadata metadata,
        RejectReversalRequest request,
        CancellationToken cancellationToken);

    Task<ReadResult<ReversalProposalView>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid proposalId,
        CancellationToken cancellationToken);

    Task<ReadResult<ReversalProposalPage>> PendingAuthorizedAsync(
        FinanceContext context,
        CancellationToken cancellationToken);
}

public enum ReversalAllocationOutcome
{
    NoAllocation,
    HasAllocation,
    Denied,
    Unavailable
}

public sealed record ReversalAllocationAnswer(ReversalAllocationOutcome Outcome, string? Code = null);

/// <summary>
/// FIN-T06/B1 owner port. Approval must receive <see cref="ReversalAllocationOutcome.NoAllocation"/>
/// before creating a reversal; every other answer refuses without a write.
/// </summary>
public interface IFinanceReversalAllocationGate
{
    Task<ReversalAllocationAnswer> CheckAsync(
        FinanceContext context,
        Guid originalSourceId,
        CancellationToken cancellationToken);
}

/// <summary>Actual immutable native command receipt; never reconstructed from the current proposal.</summary>
public sealed record ReceiptReversalDecisionReceiptV1(Guid CompanyId, Guid ProposalId, Guid OriginalSourceId,
    string CommandName, string IdempotencyKeyHash, string PayloadHash, string ActorId, string ExpectedVersion,
    string ResultVersion, string OriginalCorrelationId, DateTimeOffset RecordedAtUtc, string RecordedFactsDigest);

/// <summary>Immutable original receipt facts only: no bank card, party name, balance or general browse access.</summary>
public sealed record ReceiptReversalOriginalReceiptV1(Guid SourceId, string SourceVersion, string? DocumentNumber,
    Guid PartyId, Guid CompanyAccountId, Guid TreasuryAccountId, Guid CurrencyId, string AmountText,
    DateOnly OccurredOn, DateOnly? ValueOn, string? PosCommissionAmountText, string? PosBankNetAmountText,
    PosCommissionBearer? PosCommissionBearer, Guid OriginalEventId, string RecordedBy, DateTimeOffset RecordedAtUtc);

public sealed record ReceiptReversalDecisionDetailV1(Guid CompanyId, ReversalProposalView Proposal,
    ReceiptReversalOriginalReceiptV1 OriginalReceipt, string TreasuryAccountLabel);

/// <summary>Approval-only human purpose read. Native proposer-or-approver general reads retain their contract.</summary>
public interface IReceiptReversalDecisionReadV1
{
    Task<ReadResult<ReceiptReversalDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken);
}
