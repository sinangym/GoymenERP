using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal enum ReversalPolicyFailure
{
    InvalidRequest,
    VersionConflict,
    SourceNotRecorded,
    ProposalNotPending,
    ApproverIsProposer
}

internal sealed class ReversalPolicyException(ReversalPolicyFailure failure) : InvalidOperationException
{
    public ReversalPolicyFailure Failure { get; } = failure;
}

internal sealed record ApprovedReversalRows(ReceiptDraftRow Source, MoneyEventRow Event);

/// <summary>
/// Deterministic FIN-T06/B1-B2 state transitions. Authorization, allocation and
/// period checks happen before these methods; their successful rows are saved in
/// one transaction with command and audit records by the application service.
/// </summary>
internal static class ReversalProposalPolicy
{
    public static ReversalProposalRow Propose(
        ReceiptDraftRow original,
        ProposeReversalRequest request,
        Guid proposalId,
        string actorId,
        DateTimeOffset proposedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(request);
        if (proposalId == Guid.Empty || request.SourceId != original.Id || string.IsNullOrWhiteSpace(actorId)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.InvalidRequest);
        }

        EnsureVersion(request.ExpectedVersion, original.Version);
        if (original.Stage != ReceiptStages.Recorded)
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.SourceNotRecorded);
        }

        return new ReversalProposalRow
        {
            Id = proposalId,
            CompanyId = original.CompanyId,
            OriginalSourceId = original.Id,
            OccurredOn = request.OccurredOn,
            Reason = request.Reason.Trim(),
            Status = ReversalProposalStatuses.Pending,
            ProposedBy = actorId,
            ProposedAtUtc = proposedAtUtc,
            Version = 1,
        };
    }

    public static ApprovedReversalRows Approve(
        ReceiptDraftRow original,
        ReversalProposalRow proposal,
        ApproveReversalRequest request,
        Guid reversalSourceId,
        Guid reversalEventId,
        string actorId,
        string correlationId,
        DateTimeOffset recordedAtUtc)
    {
        ValidateDecision(original, proposal, request.ProposalId, request.ExpectedVersion, actorId);
        if (reversalSourceId == Guid.Empty || reversalEventId == Guid.Empty || reversalSourceId == original.Id
            || string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.InvalidRequest);
        }

        var source = new ReceiptDraftRow
        {
            Id = reversalSourceId,
            CompanyId = original.CompanyId,
            Version = 1,
            PartyId = original.PartyId,
            CompanyAccountId = original.CompanyAccountId,
            TreasuryAccountId = original.TreasuryAccountId,
            CurrencyId = original.CurrencyId,
            AmountText = original.AmountText,
            ProfileId = original.ProfileId,
            ProfileVersion = original.ProfileVersion,
            OccurredOn = proposal.OccurredOn,
            ValueOn = null,
            BankReference = original.BankReference,
            Description = proposal.Reason,
            Stage = ReceiptStages.Recorded,
            CreatedAtUtc = recordedAtUtc,
            CreatedBy = actorId,
            UpdatedAtUtc = recordedAtUtc,
            UpdatedBy = actorId,
            RecordedAtUtc = recordedAtUtc,
            RecordedBy = actorId,
            // DEC-FIN-014: a reversed POS collection is reversed with the same gross, commission and net facts.
            PosCommissionAmountText = original.PosCommissionAmountText,
            PosBankNetAmountText = original.PosBankNetAmountText,
            PosCommissionBearer = original.PosCommissionBearer,
        };
        var moneyEvent = new MoneyEventRow
        {
            Id = reversalEventId,
            CompanyId = original.CompanyId,
            SourceId = reversalSourceId,
            Kind = MoneyEventKinds.ManualBankReceiptReversed,
            PartyId = original.PartyId,
            CompanyAccountId = original.CompanyAccountId,
            TreasuryAccountId = original.TreasuryAccountId,
            CurrencyId = original.CurrencyId,
            AmountText = original.AmountText,
            ProfileId = original.ProfileId,
            ProfileVersion = original.ProfileVersion,
            OccurredOn = proposal.OccurredOn,
            ValueOn = null,
            RecordedAtUtc = recordedAtUtc,
            RecordedBy = actorId,
            CorrelationId = correlationId,
            PosCommissionAmountText = original.PosCommissionAmountText,
            PosBankNetAmountText = original.PosBankNetAmountText,
            PosCommissionBearer = original.PosCommissionBearer,
        };

        proposal.Status = ReversalProposalStatuses.Approved;
        proposal.DecidedBy = actorId;
        proposal.DecidedAtUtc = recordedAtUtc;
        proposal.DecisionReason = null;
        proposal.ReversalSourceId = reversalSourceId;
        proposal.Version++;
        return new ApprovedReversalRows(source, moneyEvent);
    }

    public static void Reject(
        ReceiptDraftRow original,
        ReversalProposalRow proposal,
        RejectReversalRequest request,
        string actorId,
        DateTimeOffset decidedAtUtc)
    {
        ValidateDecision(original, proposal, request.ProposalId, request.ExpectedVersion, actorId);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
        {
            throw new ReversalPolicyException(ReversalPolicyFailure.InvalidRequest);
        }

        proposal.Status = ReversalProposalStatuses.Rejected;
        proposal.DecidedBy = actorId;
        proposal.DecidedAtUtc = decidedAtUtc;
        proposal.DecisionReason = request.Reason.Trim();
        proposal.ReversalSourceId = null;
        proposal.Version++;
    }

    private static void ValidateDecision(
        ReceiptDraftRow original,
        ReversalProposalRow proposal,
        Guid proposalId,
        string expectedVersion,
        string actorId)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposalId == Guid.Empty || proposal.Id != proposalId || proposal.CompanyId != original.CompanyId
            || proposal.OriginalSourceId != original.Id || string.IsNullOrWhiteSpace(actorId))
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
