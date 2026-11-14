namespace GoymenERP.Modules.Finance.Contracts;

// API-23: maker-checker reversal of one RECORDED manual outgoing bank payment. The recorded payment and its outgoing
// money event never change; approval appends one immutable counter-event (its own ledger row on the reverse side).
// A payment applied to a payable open item cannot be reversed until every allocation is released (DEC-FIN-006).

/// <summary>Propose the reversal of a recorded payment; <paramref name="ExpectedVersion"/> is the payment's version.</summary>
public sealed record ProposePaymentReversalRequest(
    Guid PaymentId,
    string ExpectedVersion,
    DateOnly OccurredOn,
    string Reason);

public sealed record ApprovePaymentReversalRequest(Guid ProposalId, string ExpectedVersion);

public sealed record RejectPaymentReversalRequest(Guid ProposalId, string ExpectedVersion, string Reason);

/// <summary>
/// Persistent API-23 proposal projection. <paramref name="ReversalEventId"/> is set only after approval and names the
/// appended counter-event.
/// </summary>
public sealed record PaymentReversalProposalView(
    Guid Id,
    Guid PaymentId,
    DateOnly OccurredOn,
    string Reason,
    ReversalProposalStatus Status,
    string ProposedBy,
    DateTimeOffset ProposedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason,
    Guid? ReversalEventId,
    string Version);

public sealed record PaymentReversalProposalCommandResult(
    CommandOutcome Outcome,
    PaymentReversalProposalView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

public sealed record PaymentReversalProposalPage(IReadOnlyList<PaymentReversalProposalView> Items);

public interface IPaymentReversalProposalService
{
    Task<PaymentReversalProposalCommandResult> ProposeAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ProposePaymentReversalRequest request,
        CancellationToken cancellationToken);

    Task<PaymentReversalProposalCommandResult> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ApprovePaymentReversalRequest request,
        CancellationToken cancellationToken);

    Task<PaymentReversalProposalCommandResult> RejectAsync(
        FinanceContext context,
        CommandMetadata metadata,
        RejectPaymentReversalRequest request,
        CancellationToken cancellationToken);

    Task<ReadResult<PaymentReversalProposalView>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid proposalId,
        CancellationToken cancellationToken);

    Task<ReadResult<PaymentReversalProposalPage>> PendingAuthorizedAsync(
        FinanceContext context,
        CancellationToken cancellationToken);
}
