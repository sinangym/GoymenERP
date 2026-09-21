namespace GoymenERP.Modules.Finance.Contracts;

public sealed record ReversalProposalCommandResult(
    CommandOutcome Outcome,
    ReversalProposalView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

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
