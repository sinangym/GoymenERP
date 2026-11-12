using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// API-23 maker-checker transitions for the reversal of one recorded manual bank payment. The recorded payment and
/// its outgoing event never change; approval appends one counter-event. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PaymentReversalProposalPolicyTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Proposal_keeps_the_recorded_payment_untouched_and_owns_the_requested_occurrence_date()
    {
        var payment = Payment();
        var proposalId = Guid.NewGuid();

        var proposal = PaymentReversalProposalPolicy.Propose(payment,
            new ProposePaymentReversalRequest(payment.Id, "3", new DateOnly(2026, 11, 12), " Hatalı ödeme "),
            proposalId, "maker", At);

        Assert.Equal((proposalId, CompanyId, payment.Id, new DateOnly(2026, 11, 12), "Hatalı ödeme", "maker", 1L),
            (proposal.Id, proposal.CompanyId, proposal.PaymentId, proposal.OccurredOn, proposal.Reason,
                proposal.ProposedBy, proposal.Version));
        Assert.Equal(ReversalProposalStatuses.Pending, proposal.Status);
        Assert.Null(proposal.ReversalEventId);
        Assert.Equal((ManualBankPaymentStatuses.Recorded, 3L, "1250.50"),
            (payment.Status, payment.Version, payment.AmountText));
    }

    [Theory]
    [InlineData(ManualBankPaymentStatuses.Draft)]
    [InlineData(ManualBankPaymentStatuses.Approved)]
    public void Only_a_recorded_payment_can_be_proposed_for_reversal(int status)
    {
        var payment = Payment();
        payment.Status = status;

        var error = Assert.Throws<ReversalPolicyException>(() => PaymentReversalProposalPolicy.Propose(payment,
            new ProposePaymentReversalRequest(payment.Id, "3", new DateOnly(2026, 11, 12), "neden"),
            Guid.NewGuid(), "maker", At));

        Assert.Equal(ReversalPolicyFailure.SourceNotRecorded, error.Failure);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("03")]
    [InlineData("+3")]
    [InlineData("")]
    public void A_stale_or_non_canonical_payment_version_is_a_conflict(string version)
    {
        var payment = Payment();

        var error = Assert.Throws<ReversalPolicyException>(() => PaymentReversalProposalPolicy.Propose(payment,
            new ProposePaymentReversalRequest(payment.Id, version, new DateOnly(2026, 11, 12), "neden"),
            Guid.NewGuid(), "maker", At));

        Assert.Equal(ReversalPolicyFailure.VersionConflict, error.Failure);
    }

    [Theory]
    [InlineData("blank-reason")]
    [InlineData("long-reason")]
    [InlineData("other-payment")]
    [InlineData("empty-id")]
    [InlineData("blank-actor")]
    public void An_invalid_proposal_request_is_refused(string fault)
    {
        var payment = Payment();
        var reason = fault switch
        {
            "blank-reason" => "   ",
            "long-reason" => new string('x', 1001),
            _ => "neden",
        };
        var paymentId = fault == "other-payment" ? Guid.NewGuid() : payment.Id;
        var proposalId = fault == "empty-id" ? Guid.Empty : Guid.NewGuid();
        var actor = fault == "blank-actor" ? " " : "maker";

        var error = Assert.Throws<ReversalPolicyException>(() => PaymentReversalProposalPolicy.Propose(payment,
            new ProposePaymentReversalRequest(paymentId, "3", new DateOnly(2026, 11, 12), reason),
            proposalId, actor, At));

        Assert.Equal(ReversalPolicyFailure.InvalidRequest, error.Failure);
    }

    [Fact]
    public void A_reversal_dated_before_the_payment_is_refused_and_the_payment_date_itself_is_allowed()
    {
        var payment = Payment();

        var early = Assert.Throws<ReversalPolicyException>(() => PaymentReversalProposalPolicy.Propose(payment,
            new ProposePaymentReversalRequest(payment.Id, "3", new DateOnly(2026, 11, 4), "Hatalı ödeme"),
            Guid.NewGuid(), "maker", At));
        var sameDay = PaymentReversalProposalPolicy.Propose(payment,
            new ProposePaymentReversalRequest(payment.Id, "3", payment.OccurredOn, "Hatalı ödeme"),
            Guid.NewGuid(), "maker", At);

        Assert.Equal(ReversalPolicyFailure.OccursBeforeSource, early.Failure);
        Assert.Equal(payment.OccurredOn, sameDay.OccurredOn);
    }

    [Fact]
    public void Approval_refuses_a_proposal_dated_before_the_payment_and_leaves_it_pending()
    {
        var payment = Payment();
        var proposal = Proposal(payment, "maker");
        proposal.OccurredOn = new DateOnly(2026, 11, 4);

        var error = Assert.Throws<ReversalPolicyException>(() => PaymentReversalProposalPolicy.Approve(payment,
            Event(payment), proposal, new ApprovePaymentReversalRequest(proposal.Id, "1"), Guid.NewGuid(), "checker",
            "corr-1", At));

        Assert.Equal(ReversalPolicyFailure.OccursBeforeSource, error.Failure);
        Assert.Equal((ReversalProposalStatuses.Pending, 1L), (proposal.Status, proposal.Version));
        Assert.Null(proposal.ReversalEventId);
    }

    [Fact]
    public void Approval_by_another_user_appends_one_counter_event_that_copies_the_original_money_fields()
    {
        var payment = Payment();
        var original = Event(payment);
        var proposal = Proposal(payment, "maker");
        var reversalId = Guid.NewGuid();

        var reversal = PaymentReversalProposalPolicy.Approve(payment, original, proposal,
            new ApprovePaymentReversalRequest(proposal.Id, "1"), reversalId, "checker", "corr-1", At);

        Assert.Equal((reversalId, CompanyId, payment.Id, original.Id, proposal.Id), (reversal.Id, reversal.CompanyId,
            reversal.PaymentId, reversal.OriginalEventId, reversal.ProposalId));
        Assert.Equal((original.PartyId, original.CompanyAccountId, original.TreasuryAccountId, original.CurrencyId,
                original.AmountText, original.ProfileId, original.ProfileVersion),
            (reversal.PartyId, reversal.CompanyAccountId, reversal.TreasuryAccountId, reversal.CurrencyId,
                reversal.AmountText, reversal.ProfileId, reversal.ProfileVersion));
        Assert.Equal((proposal.OccurredOn, At, "checker", "corr-1"),
            (reversal.OccurredOn, reversal.RecordedAtUtc, reversal.RecordedBy, reversal.CorrelationId));
        Assert.Equal((ReversalProposalStatuses.Approved, "checker", (DateTimeOffset?)At, (string?)null,
                (Guid?)reversalId, 2L),
            (proposal.Status, proposal.DecidedBy, proposal.DecidedAtUtc, proposal.DecisionReason,
                proposal.ReversalEventId, proposal.Version));
        Assert.Equal((ManualBankPaymentStatuses.Recorded, 3L), (payment.Status, payment.Version));
        Assert.Equal(new DateOnly(2026, 11, 5), original.OccurredOn);
    }

    [Fact]
    public void Maker_can_neither_approve_nor_reject_their_own_proposal()
    {
        var payment = Payment();
        var proposal = Proposal(payment, "maker");

        var approve = Assert.Throws<ReversalPolicyException>(() => PaymentReversalProposalPolicy.Approve(payment,
            Event(payment), proposal, new ApprovePaymentReversalRequest(proposal.Id, "1"), Guid.NewGuid(), "maker",
            "corr-1", At));
        var reject = Assert.Throws<ReversalPolicyException>(() => PaymentReversalProposalPolicy.Reject(payment,
            proposal, new RejectPaymentReversalRequest(proposal.Id, "1", "maker karari"), "maker", At));

        Assert.Equal(ReversalPolicyFailure.ApproverIsProposer, approve.Failure);
        Assert.Equal(ReversalPolicyFailure.ApproverIsProposer, reject.Failure);
        Assert.Equal((ReversalProposalStatuses.Pending, 1L), (proposal.Status, proposal.Version));
        Assert.Null(proposal.DecidedBy);
        Assert.Null(proposal.ReversalEventId);
    }

    [Fact]
    public void Rejection_records_the_reason_and_creates_no_counter_event()
    {
        var payment = Payment();
        var proposal = Proposal(payment, "maker");

        PaymentReversalProposalPolicy.Reject(payment, proposal,
            new RejectPaymentReversalRequest(proposal.Id, "1", " Ödeme doğru "), "checker", At);

        Assert.Equal((ReversalProposalStatuses.Rejected, "checker", "Ödeme doğru", 2L),
            (proposal.Status, proposal.DecidedBy, proposal.DecisionReason, proposal.Version));
        Assert.Null(proposal.ReversalEventId);
    }

    [Theory]
    [InlineData(ReversalProposalStatuses.Approved)]
    [InlineData(ReversalProposalStatuses.Rejected)]
    public void A_decided_proposal_cannot_be_decided_again(int status)
    {
        var payment = Payment();
        var proposal = Proposal(payment, "maker");
        proposal.Status = status;

        var error = Assert.Throws<ReversalPolicyException>(() => PaymentReversalProposalPolicy.Reject(payment,
            proposal, new RejectPaymentReversalRequest(proposal.Id, "1", "neden"), "checker", At));

        Assert.Equal(ReversalPolicyFailure.ProposalNotPending, error.Failure);
    }

    [Theory]
    [InlineData("stale-version")]
    [InlineData("other-proposal")]
    [InlineData("event-of-other-payment")]
    [InlineData("reversal-id-equals-original")]
    [InlineData("blank-correlation")]
    public void Approval_refuses_a_stale_or_mismatched_request(string fault)
    {
        var payment = Payment();
        var original = Event(payment);
        var proposal = Proposal(payment, "maker");
        var expected = fault == "stale-version" ? "2" : "1";
        var proposalId = fault == "other-proposal" ? Guid.NewGuid() : proposal.Id;
        if (fault == "event-of-other-payment")
        {
            original.PaymentId = Guid.NewGuid();
        }
        var reversalId = fault == "reversal-id-equals-original" ? original.Id : Guid.NewGuid();
        var correlation = fault == "blank-correlation" ? " " : "corr-1";

        var error = Assert.Throws<ReversalPolicyException>(() => PaymentReversalProposalPolicy.Approve(payment,
            original, proposal, new ApprovePaymentReversalRequest(proposalId, expected), reversalId, "checker",
            correlation, At));

        Assert.Equal(fault == "stale-version" ? ReversalPolicyFailure.VersionConflict : ReversalPolicyFailure.InvalidRequest,
            error.Failure);
        Assert.Equal((ReversalProposalStatuses.Pending, 1L), (proposal.Status, proposal.Version));
    }

    [Fact]
    public void The_allocation_barrier_closes_an_untouched_payment_to_zero_unallocated()
    {
        var payment = Payment();

        var fresh = PaymentReversalProposalPolicy.PrepareBarrier(payment, null);
        var existing = PaymentReversalProposalPolicy.PrepareBarrier(payment, State(payment, "1250.50", 4));

        Assert.Equal(("1250.50", 1L), (fresh.OriginalAmount, fresh.Version));
        Assert.True(FinanceExactDecimalText.Equal("0", fresh.UnallocatedAmount));
        Assert.Equal(5L, existing.Version);
        Assert.True(FinanceExactDecimalText.Equal("0", existing.UnallocatedAmount));
    }

    [Fact]
    public void The_allocation_barrier_refuses_a_partly_allocated_or_foreign_state()
    {
        var payment = Payment();
        var partly = State(payment, "1000", 2);
        var foreign = State(payment, "1250.50", 2);
        foreign.CompanyId = Guid.NewGuid();

        foreach (var state in new[] { partly, foreign })
        {
            var error = Assert.Throws<FinanceAllocationPolicyException>(() =>
                PaymentReversalProposalPolicy.PrepareBarrier(payment, state));
            Assert.Equal("finance_allocation_state_invalid", error.Code);
        }
    }

    private static ManualBankPaymentRow Payment() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyId, Version = 3, PartyId = Guid.NewGuid(),
        CompanyAccountId = Guid.NewGuid(), TreasuryAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(),
        AmountText = "1250.50", ProfileId = "SENTETIK-profile", ProfileVersion = "1",
        OccurredOn = new DateOnly(2026, 11, 5), Status = ManualBankPaymentStatuses.Recorded,
        PreparedBy = "preparer", PreparedAtUtc = At, ApprovedBy = "approver", ApprovedAtUtc = At,
        RecordedBy = "recorder", RecordedAtUtc = At, BankResultConfirmed = true,
    };

    private static OutgoingMoneyEventRow Event(ManualBankPaymentRow payment) => new()
    {
        Id = Guid.NewGuid(), CompanyId = payment.CompanyId, PaymentId = payment.Id, PartyId = payment.PartyId,
        CompanyAccountId = payment.CompanyAccountId, TreasuryAccountId = payment.TreasuryAccountId,
        CurrencyId = payment.CurrencyId, AmountText = payment.AmountText, ProfileId = payment.ProfileId,
        ProfileVersion = payment.ProfileVersion, OccurredOn = payment.OccurredOn, RecordedAtUtc = At,
        RecordedBy = "recorder", CorrelationId = "corr-0",
    };

    private static PaymentReversalProposalRow Proposal(ManualBankPaymentRow payment, string maker) =>
        PaymentReversalProposalPolicy.Propose(payment,
            new ProposePaymentReversalRequest(payment.Id, payment.Version.ToString(
                System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2026, 11, 12), "Hatalı ödeme"),
            Guid.NewGuid(), maker, At);

    private static FinancePaymentAllocationStateRow State(ManualBankPaymentRow payment, string unallocated, long version) => new()
    {
        CompanyId = payment.CompanyId, PaymentId = payment.Id, CurrencyId = payment.CurrencyId,
        OriginalAmountText = payment.AmountText, UnallocatedAmountText = unallocated, Version = version,
        UpdatedBy = "SENTETIK", UpdatedAtUtc = At,
    };
}
