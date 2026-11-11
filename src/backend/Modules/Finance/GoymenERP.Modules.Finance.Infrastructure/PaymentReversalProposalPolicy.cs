using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal sealed record FinancePaymentReversalBarrier(string OriginalAmount, string UnallocatedAmount, long Version);

/// <summary>
/// Deterministic API-23 state transitions for the reversal of one recorded manual bank payment. Authorization,
/// allocation and period checks happen before these methods; the application service saves the rows in one
/// transaction with the command and audit records. Failures reuse <see cref="ReversalPolicyFailure"/>.
/// </summary>
internal static class PaymentReversalProposalPolicy
{
    private const int ReasonMaximumLength = 1000;

    public static PaymentReversalProposalRow Propose(
        ManualBankPaymentRow payment,
        ProposePaymentReversalRequest request,
        Guid proposalId,
        string actorId,
        DateTimeOffset proposedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(request);
        if (proposalId == Guid.Empty || request.PaymentId != payment.Id || string.IsNullOrWhiteSpace(actorId)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > ReasonMaximumLength)
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.InvalidRequest);
        }

        EnsureVersion(request.ExpectedVersion, payment.Version);
        if (payment.Status != ManualBankPaymentStatuses.Recorded)
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.SourceNotRecorded);
        }

        return new PaymentReversalProposalRow
        {
            Id = proposalId,
            CompanyId = payment.CompanyId,
            PaymentId = payment.Id,
            OccurredOn = request.OccurredOn,
            Reason = request.Reason.Trim(),
            Status = ReversalProposalStatuses.Pending,
            ProposedBy = actorId,
            ProposedAtUtc = proposedAtUtc,
            Version = 1,
        };
    }

    public static OutgoingMoneyEventReversalRow Approve(
        ManualBankPaymentRow payment,
        OutgoingMoneyEventRow original,
        PaymentReversalProposalRow proposal,
        ApprovePaymentReversalRequest request,
        Guid reversalEventId,
        string actorId,
        string correlationId,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDecision(payment, proposal, request.ProposalId, request.ExpectedVersion, actorId);
        ArgumentNullException.ThrowIfNull(original);
        if (reversalEventId == Guid.Empty || reversalEventId == original.Id || string.IsNullOrWhiteSpace(correlationId)
            || original.CompanyId != payment.CompanyId || original.PaymentId != payment.Id
            || payment.Status != ManualBankPaymentStatuses.Recorded)
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.InvalidRequest);
        }

        var reversal = new OutgoingMoneyEventReversalRow
        {
            Id = reversalEventId,
            CompanyId = original.CompanyId,
            PaymentId = original.PaymentId,
            OriginalEventId = original.Id,
            ProposalId = proposal.Id,
            PartyId = original.PartyId,
            CompanyAccountId = original.CompanyAccountId,
            TreasuryAccountId = original.TreasuryAccountId,
            CurrencyId = original.CurrencyId,
            AmountText = original.AmountText,
            ProfileId = original.ProfileId,
            ProfileVersion = original.ProfileVersion,
            OccurredOn = proposal.OccurredOn,
            RecordedAtUtc = recordedAtUtc,
            RecordedBy = actorId,
            CorrelationId = correlationId,
        };

        proposal.Status = ReversalProposalStatuses.Approved;
        proposal.DecidedBy = actorId;
        proposal.DecidedAtUtc = recordedAtUtc;
        proposal.DecisionReason = null;
        proposal.ReversalEventId = reversalEventId;
        proposal.Version++;
        return reversal;
    }

    public static void Reject(
        ManualBankPaymentRow payment,
        PaymentReversalProposalRow proposal,
        RejectPaymentReversalRequest request,
        string actorId,
        DateTimeOffset decidedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDecision(payment, proposal, request.ProposalId, request.ExpectedVersion, actorId);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > ReasonMaximumLength)
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.InvalidRequest);
        }

        proposal.Status = ReversalProposalStatuses.Rejected;
        proposal.DecidedBy = actorId;
        proposal.DecidedAtUtc = decidedAtUtc;
        proposal.DecisionReason = request.Reason.Trim();
        proposal.ReversalEventId = null;
        proposal.Version++;
    }

    /// <summary>
    /// The payment-side allocation barrier: a reversed payment can never be allocated again. An existing state row
    /// must be untouched (unallocated equals original); the barrier sets the unallocated amount to zero.
    /// </summary>
    public static FinancePaymentReversalBarrier PrepareBarrier(
        ManualBankPaymentRow payment,
        FinancePaymentAllocationStateRow? state)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.Status != ManualBankPaymentStatuses.Recorded
            || !FinanceExactDecimalText.TryParsePositive(payment.AmountText, out var amount))
        {
            throw new FinanceAllocationPolicyException("paymentId", "finance_allocation_state_invalid");
        }

        if (state is not null
            && (state.CompanyId != payment.CompanyId || state.PaymentId != payment.Id
                || state.CurrencyId != payment.CurrencyId || state.Version < 1
                || !FinanceExactDecimalText.Equal(state.OriginalAmountText, payment.AmountText)
                || !FinanceExactDecimalText.Equal(state.UnallocatedAmountText, payment.AmountText)))
        {
            throw new FinanceAllocationPolicyException("paymentId", "finance_allocation_state_invalid");
        }

        return new FinancePaymentReversalBarrier(
            payment.AmountText,
            amount.Subtract(amount).Text,
            state is null ? 1 : checked(state.Version + 1));
    }

    private static void ValidateDecision(
        ManualBankPaymentRow payment,
        PaymentReversalProposalRow proposal,
        Guid proposalId,
        string expectedVersion,
        string actorId)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposalId == Guid.Empty || proposal.Id != proposalId || proposal.CompanyId != payment.CompanyId
            || proposal.PaymentId != payment.Id || string.IsNullOrWhiteSpace(actorId))
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.InvalidRequest);
        }

        EnsureVersion(expectedVersion, proposal.Version);
        if (proposal.Status != ReversalProposalStatuses.Pending)
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.ProposalNotPending);
        }
        if (string.Equals(proposal.ProposedBy, actorId, StringComparison.Ordinal))
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.ApproverIsProposer);
        }
    }

    private static void EnsureVersion(string expectedVersion, long actualVersion)
    {
        if (!long.TryParse(expectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0 || parsed != actualVersion
            || !string.Equals(parsed.ToString(CultureInfo.InvariantCulture), expectedVersion, StringComparison.Ordinal))
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.VersionConflict);
        }
    }
}
