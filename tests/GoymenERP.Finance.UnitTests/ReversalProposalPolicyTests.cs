using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

public sealed class ReversalProposalPolicyTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Proposal_keeps_the_original_immutable_and_owns_the_requested_occurrence_date()
    {
        var original = Original();
        var originalDate = original.OccurredOn;
        var proposalId = Guid.NewGuid();

        var proposal = ReversalProposalPolicy.Propose(original,
            new ProposeReversalRequest(original.Id, "1", new DateOnly(2026, 9, 22), " Hatalı tahsilat "),
            proposalId, "maker", At);

        Assert.Equal((proposalId, original.Id, new DateOnly(2026, 9, 22), "Hatalı tahsilat", "maker", 1L),
            (proposal.Id, proposal.OriginalSourceId, proposal.OccurredOn, proposal.Reason, proposal.ProposedBy, proposal.Version));
        Assert.Equal(originalDate, original.OccurredOn);
        Assert.Equal(ReceiptStages.Recorded, original.Stage);
    }

    [Fact]
    public void Maker_cannot_approve_their_own_proposal()
    {
        var original = Original();
        var proposal = Proposal(original, "maker");

        var error = Assert.Throws<ReversalPolicyException>(() => ReversalProposalPolicy.Approve(
            original, proposal, new ApproveReversalRequest(proposal.Id, "1"), Guid.NewGuid(), Guid.NewGuid(),
            "maker", "corr-1", At));

        Assert.Equal(ReversalPolicyFailure.ApproverIsProposer, error.Failure);
        Assert.Equal(ReversalProposalStatuses.Pending, proposal.Status);
        Assert.Null(proposal.ReversalSourceId);
    }

    [Fact]
    public void Maker_cannot_reject_their_own_proposal_either()
    {
        var original = Original();
        var proposal = Proposal(original, "maker");

        var error = Assert.Throws<ReversalPolicyException>(() => ReversalProposalPolicy.Reject(
            original, proposal, new RejectReversalRequest(proposal.Id, "1", "maker karari"), "maker", At));

        Assert.Equal(ReversalPolicyFailure.ApproverIsProposer, error.Failure);
        Assert.Equal((ReversalProposalStatuses.Pending, 1L), (proposal.Status, proposal.Version));
        Assert.Null(proposal.DecidedBy);
        Assert.Null(proposal.DecisionReason);
        Assert.Null(proposal.ReversalSourceId);
    }

    [Fact]
    public void Approval_creates_a_distinct_recorded_source_and_reversal_event_without_editing_original()
    {
        var original = Original();
        var originalVersion = original.Version;
        var originalDate = original.OccurredOn;
        var proposal = Proposal(original, "maker");
        var reversalId = Guid.NewGuid();

        var rows = ReversalProposalPolicy.Approve(original, proposal,
            new ApproveReversalRequest(proposal.Id, "1"), reversalId, Guid.NewGuid(), "checker", "corr-2", At);

        Assert.Equal((reversalId, ReceiptStages.Recorded, proposal.OccurredOn, original.AmountText),
            (rows.Source.Id, rows.Source.Stage, rows.Source.OccurredOn, rows.Source.AmountText));
        Assert.Equal((reversalId, MoneyEventKinds.ManualBankReceiptReversed, original.AmountText, proposal.OccurredOn),
            (rows.Event.SourceId, rows.Event.Kind, rows.Event.AmountText, rows.Event.OccurredOn));
        Assert.Equal((ReversalProposalStatuses.Approved, "checker", reversalId, 2L),
            (proposal.Status, proposal.DecidedBy, proposal.ReversalSourceId, proposal.Version));
        Assert.Equal((originalVersion, originalDate, ReceiptStages.Recorded),
            (original.Version, original.OccurredOn, original.Stage));
    }

    [Fact]
    public void A_reversed_POS_collection_is_reversed_with_its_gross_commission_and_net_facts()
    {
        var original = Original();
        original.PosCommissionAmountText = "2.25";
        original.PosBankNetAmountText = "98.00";
        original.PosCommissionBearer = PosCommissionBearers.Customer;
        var proposal = Proposal(original, "maker");

        var rows = ReversalProposalPolicy.Approve(original, proposal,
            new ApproveReversalRequest(proposal.Id, "1"), Guid.NewGuid(), Guid.NewGuid(), "checker", "corr-pos", At);

        Assert.Equal(("100.25", "2.25", "98.00", (int?)PosCommissionBearers.Customer),
            (rows.Source.AmountText, rows.Source.PosCommissionAmountText, rows.Source.PosBankNetAmountText,
                rows.Source.PosCommissionBearer));
        Assert.Equal(("100.25", "2.25", "98.00", (int?)PosCommissionBearers.Customer),
            (rows.Event.AmountText, rows.Event.PosCommissionAmountText, rows.Event.PosBankNetAmountText,
                rows.Event.PosCommissionBearer));
    }

    [Fact]
    public void Rejection_is_separate_and_keeps_the_reversal_link_empty()
    {
        var original = Original();
        var proposal = Proposal(original, "maker");

        ReversalProposalPolicy.Reject(original, proposal,
            new RejectReversalRequest(proposal.Id, "1", " Belge doğrulanamadı "), "checker", At);

        Assert.Equal((ReversalProposalStatuses.Rejected, "checker", "Belge doğrulanamadı", 2L),
            (proposal.Status, proposal.DecidedBy, proposal.DecisionReason, proposal.Version));
        Assert.Null(proposal.ReversalSourceId);
    }

    [Theory]
    [InlineData("01")]
    [InlineData("2")]
    [InlineData("x")]
    public void Expected_version_must_be_the_exact_current_canonical_value(string expectedVersion)
    {
        var original = Original();
        var error = Assert.Throws<ReversalPolicyException>(() => ReversalProposalPolicy.Propose(original,
            new ProposeReversalRequest(original.Id, expectedVersion, new DateOnly(2026, 9, 22), "neden"),
            Guid.NewGuid(), "maker", At));
        Assert.Equal(ReversalPolicyFailure.VersionConflict, error.Failure);
    }

    private static ReversalProposalRow Proposal(ReceiptDraftRow original, string maker) =>
        ReversalProposalPolicy.Propose(original,
            new ProposeReversalRequest(original.Id, "1", new DateOnly(2026, 9, 22), "neden"),
            Guid.NewGuid(), maker, At.AddMinutes(-1));

    private static ReceiptDraftRow Original() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyId, Version = 1,
        PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), TreasuryAccountId = Guid.NewGuid(),
        CurrencyId = Guid.NewGuid(), AmountText = "100.25", ProfileId = Guid.NewGuid().ToString("D"),
        ProfileVersion = "1", OccurredOn = new DateOnly(2026, 9, 20), ValueOn = new DateOnly(2026, 9, 21),
        BankReference = "BANK-1", Description = "Tahsilat", Stage = ReceiptStages.Recorded,
        CreatedAtUtc = At.AddDays(-1), CreatedBy = "recorder", UpdatedAtUtc = At.AddDays(-1), UpdatedBy = "recorder",
        RecordedAtUtc = At.AddDays(-1), RecordedBy = "recorder",
    };
}
