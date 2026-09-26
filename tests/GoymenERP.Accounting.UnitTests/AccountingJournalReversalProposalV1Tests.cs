using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingJournalReversalProposalV1Tests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProposalId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceJournalId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PeriodId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ProposerId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid CheckerId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ReversalJournalId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset ProposedAtUtc = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DecidedAtUtc = new(2026, 9, 25, 8, 15, 0, TimeSpan.Zero);

    [Fact]
    public void Propose_creates_pending_version_one_and_preserves_source_dates()
    {
        var proposal = CreateProposal();

        Assert.Equal(ProposalId, proposal.ProposalId);
        Assert.Equal(CompanyId, proposal.CompanyId);
        Assert.Equal(SourceJournalId, proposal.SourceJournalId);
        Assert.Null(proposal.ReversalJournalId);
        Assert.Equal(PeriodId, proposal.PeriodId);
        Assert.Equal(new DateOnly(2026, 9, 24), proposal.TransactionDate);
        Assert.Equal(new DateOnly(2026, 9, 25), proposal.AccountingDate);
        Assert.Equal(new DateOnly(2026, 9, 26), proposal.ValueDate);
        Assert.Equal("Kaynak fis hatali hesaba kaydedildi.", proposal.Reason);
        Assert.Equal(ProposerId, proposal.ProposedBy);
        Assert.Equal(ProposedAtUtc, proposal.ProposedAtUtc);
        Assert.Equal(AccountingJournalReversalProposalStatusV1.Pending, proposal.Status);
        Assert.Null(proposal.DecidedBy);
        Assert.Null(proposal.DecidedAtUtc);
        Assert.Null(proposal.DecisionReason);
        Assert.Equal(1, proposal.Version);
    }

    [Fact]
    public void Maker_cannot_approve_or_reject_own_proposal()
    {
        var proposal = CreateProposal();

        var approve = Assert.Throws<AccountingJournalReversalPolicyExceptionV1>(() =>
            AccountingJournalReversalPolicyV1.Approve(
                proposal,
                new DecideAccountingJournalReversalV1(ProposalId, proposal.Version),
                ProposerId,
                ReversalJournalId,
                DecidedAtUtc));
        var reject = Assert.Throws<AccountingJournalReversalPolicyExceptionV1>(() =>
            AccountingJournalReversalPolicyV1.Reject(
                proposal,
                new DecideAccountingJournalReversalV1(ProposalId, proposal.Version, "Reddedildi."),
                ProposerId,
                DecidedAtUtc));

        Assert.Equal(AccountingJournalReversalPolicyCodesV1.MakerCheckerRequired, approve.Code);
        Assert.Equal(AccountingJournalReversalPolicyCodesV1.MakerCheckerRequired, reject.Code);
    }

    [Fact]
    public void Another_checker_approves_with_server_generated_reversal_link()
    {
        var proposal = CreateProposal();

        var approved = AccountingJournalReversalPolicyV1.Approve(
            proposal,
            new DecideAccountingJournalReversalV1(ProposalId, proposal.Version),
            CheckerId,
            ReversalJournalId,
            DecidedAtUtc);

        Assert.Equal(AccountingJournalReversalProposalStatusV1.Approved, approved.Status);
        Assert.Equal(SourceJournalId, approved.SourceJournalId);
        Assert.Equal(ReversalJournalId, approved.ReversalJournalId);
        Assert.Equal(CheckerId, approved.DecidedBy);
        Assert.Equal(DecidedAtUtc, approved.DecidedAtUtc);
        Assert.Null(approved.DecisionReason);
        Assert.Equal(2, approved.Version);
        Assert.Equal(AccountingJournalReversalProposalStatusV1.Pending, proposal.Status);
        Assert.Null(proposal.ReversalJournalId);
        Assert.Equal(1, proposal.Version);
    }

    [Fact]
    public void Reject_preserves_source_history_without_creating_reversal_identity()
    {
        var proposal = CreateProposal();

        var rejected = AccountingJournalReversalPolicyV1.Reject(
            proposal,
            new DecideAccountingJournalReversalV1(ProposalId, proposal.Version, "  Belge tekrar incelenecek.  "),
            CheckerId,
            DecidedAtUtc);

        Assert.Equal(AccountingJournalReversalProposalStatusV1.Rejected, rejected.Status);
        Assert.Equal(SourceJournalId, rejected.SourceJournalId);
        Assert.Null(rejected.ReversalJournalId);
        Assert.Equal("Belge tekrar incelenecek.", rejected.DecisionReason);
        Assert.Equal(CheckerId, rejected.DecidedBy);
        Assert.Equal(2, rejected.Version);
    }

    [Fact]
    public void Decisions_require_pending_status_and_exact_current_version()
    {
        var proposal = CreateProposal();

        var stale = Assert.Throws<AccountingJournalReversalPolicyExceptionV1>(() =>
            AccountingJournalReversalPolicyV1.Approve(
                proposal,
                new DecideAccountingJournalReversalV1(ProposalId, proposal.Version + 1),
                CheckerId,
                ReversalJournalId,
                DecidedAtUtc));
        Assert.Equal(AccountingJournalReversalPolicyCodesV1.VersionConflict, stale.Code);

        var approved = AccountingJournalReversalPolicyV1.Approve(
            proposal,
            new DecideAccountingJournalReversalV1(ProposalId, proposal.Version),
            CheckerId,
            ReversalJournalId,
            DecidedAtUtc);

        var terminal = Assert.Throws<AccountingJournalReversalPolicyExceptionV1>(() =>
            AccountingJournalReversalPolicyV1.Reject(
                approved,
                new DecideAccountingJournalReversalV1(ProposalId, approved.Version, "Tekrar karar."),
                CheckerId,
                DecidedAtUtc.AddMinutes(1)));
        Assert.Equal(AccountingJournalReversalPolicyCodesV1.NotPending, terminal.Code);
    }

    [Fact]
    public void Invalid_identity_dates_reason_and_non_utc_timestamps_fail_closed()
    {
        var request = CreateRequest();

        AssertInvalid("companyId", () => AccountingJournalReversalPolicyV1.Propose(
            Guid.Empty, ProposerId, request, ProposedAtUtc));
        AssertInvalid("transactionDate", () => AccountingJournalReversalPolicyV1.Propose(
            CompanyId, ProposerId, request with { TransactionDate = default }, ProposedAtUtc));
        AssertInvalid("reason", () => AccountingJournalReversalPolicyV1.Propose(
            CompanyId, ProposerId, request with { Reason = " " }, ProposedAtUtc));
        AssertInvalid("proposedAtUtc", () => AccountingJournalReversalPolicyV1.Propose(
            CompanyId, ProposerId, request, ProposedAtUtc.ToOffset(TimeSpan.FromHours(3))));

        var proposal = CreateProposal();
        AssertInvalid("reversalJournalId", () => AccountingJournalReversalPolicyV1.Approve(
            proposal,
            new DecideAccountingJournalReversalV1(ProposalId, proposal.Version),
            CheckerId,
            SourceJournalId,
            DecidedAtUtc));
        AssertInvalid("reason", () => AccountingJournalReversalPolicyV1.Reject(
            proposal,
            new DecideAccountingJournalReversalV1(ProposalId, proposal.Version, " "),
            CheckerId,
            DecidedAtUtc));
    }

    private static AccountingJournalReversalProposalV1 CreateProposal() =>
        AccountingJournalReversalPolicyV1.Propose(CompanyId, ProposerId, CreateRequest(), ProposedAtUtc);

    private static ProposeAccountingJournalReversalV1 CreateRequest() =>
        new(
            ProposalId,
            SourceJournalId,
            PeriodId,
            new DateOnly(2026, 9, 24),
            new DateOnly(2026, 9, 25),
            new DateOnly(2026, 9, 26),
            "  Kaynak fis hatali hesaba kaydedildi.  ");

    private static void AssertInvalid(string field, Action action)
    {
        var error = Assert.Throws<AccountingJournalReversalPolicyExceptionV1>(action);
        Assert.Equal(AccountingJournalReversalPolicyCodesV1.InvalidRequest, error.Code);
        Assert.Equal(field, error.Field);
    }
}
