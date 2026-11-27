using System.Text.Json.Serialization;

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
    IReadOnlyList<ValidationIssue> Issues)
{
    // Owner-only provenance; neither the existing five-field wire nor persisted native hashes change.
    [JsonIgnore] public PaymentReversalDecisionReceiptV1? DecisionReceipt { get; init; }
}

public sealed record PaymentReversalDecisionReceiptV1(Guid CompanyId, Guid ProposalId, Guid PaymentId,
    string CommandName, string IdempotencyKeyHash, string PayloadHash, string ActorId, string ExpectedVersion,
    string ResultVersion, string OriginalCorrelationId, DateTimeOffset RecordedAtUtc, string RecordedFactsDigest);

/// <summary>Purpose-only human facts. No bank card, IBAN, balance or general payment-read authority.</summary>
public sealed record PaymentReversalDecisionPreviewV1(Guid CompanyId, PaymentReversalProposalView Proposal,
    string? PaymentDocumentNumber, Guid PartyId, Guid CompanyAccountId, Guid TreasuryAccountId, Guid CurrencyId,
    string Amount, DateOnly PaymentOccurredOn, DateOnly? PaymentValueOn, Guid OriginalEventId,
    string PaymentRecordedBy, DateTimeOffset PaymentRecordedAtUtc, string RecordedFactsDigest, string TreasuryAccountLabel);

public interface IPaymentReversalDecisionPreviewReadV1
{
    Task<ReadResult<PaymentReversalDecisionPreviewV1>> ReadDecisionPreviewAsync(FinanceContext context, Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken);
}

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

/// <summary>Approval-only purpose read; the native proposer-or-approver general read is unchanged.</summary>
public interface IPaymentReversalDecisionReadV1
{
    Task<ReadResult<PaymentReversalProposalView>> ReadDecisionAsync(FinanceContext context, Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken);
}
