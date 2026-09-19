using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// ACC-V0: a period opening is maker≠checker (Ö4, #66 5722637822 "dönem açma … hazırlayandan farklı
/// tek yetkili onayında"); closing is one authorised act (Ö5). Synthetic identities only.
/// </summary>
public sealed class PeriodApprovalTests
{
    private static readonly DateOnly September1 = new(2026, 9, 1);
    private static readonly DateOnly September30 = new(2026, 9, 30);

    private static string CodeOf(Action action) => Assert.Throws<AccountingDomainException>(action).Code;

    private static AccountingPeriod Proposal(Guid company) =>
        AccountingPeriod.Propose(Guid.NewGuid(), company, September1, September30, AccountingWorld.Preparer, AccountingWorld.PreparedAt);

    [Fact]
    public void A_proposal_is_not_open_and_takes_no_posting_until_another_person_approves_it()
    {
        var world = new AccountingWorld();
        var period = Proposal(world.Company);

        Assert.Equal(AccountingPeriodStatus.Proposed, period.Status);
        Assert.Equal((AccountingWorld.Preparer, AccountingWorld.PreparedAt), (period.PreparedByUserId, period.PreparedAtUtc));
        Assert.Equal(AccountingErrorCodes.PeriodNotOpen, CodeOf(() => world.Ledger.Receive(world.Request("m-1"), period)));
        Assert.Empty(world.Ledger.Journals);

        period.Approve(AccountingWorld.Approver, AccountingWorld.ApprovedAt);

        Assert.Equal((AccountingPeriodStatus.Open, (Guid?)AccountingWorld.Approver, (DateTimeOffset?)AccountingWorld.ApprovedAt),
            (period.Status, period.ApprovedByUserId, period.ApprovedAtUtc));
        // The delivery refused while proposed was not committed, so it posts now.
        Assert.Equal(PostingOutcomeKind.Posted, world.Ledger.Receive(world.Request("m-1"), period).Kind);
    }

    [Fact]
    public void The_preparer_cannot_approve_their_own_period()
    {
        var period = Proposal(Guid.NewGuid());

        Assert.Equal(AccountingErrorCodes.PeriodSelfApproval,
            CodeOf(() => period.Approve(AccountingWorld.Preparer, AccountingWorld.ApprovedAt)));
        Assert.Equal(AccountingPeriodStatus.Proposed, period.Status);
        Assert.Null(period.ApprovedByUserId);
    }

    [Fact]
    public void An_approval_is_given_once_and_only_to_a_proposal()
    {
        var period = Proposal(Guid.NewGuid());
        period.Approve(AccountingWorld.Approver, AccountingWorld.ApprovedAt);

        Assert.Equal(AccountingErrorCodes.PeriodNotProposed,
            CodeOf(() => period.Approve(Guid.NewGuid(), AccountingWorld.ApprovedAt.AddHours(1))));
        Assert.Equal((Guid?)AccountingWorld.Approver, period.ApprovedByUserId);

        period.Close(AccountingWorld.Closer, AccountingWorld.ClosedAt);
        Assert.Equal(AccountingErrorCodes.PeriodNotProposed,
            CodeOf(() => period.Approve(Guid.NewGuid(), AccountingWorld.ClosedAt.AddHours(1))));
    }

    [Fact]
    public void A_proposal_cannot_be_closed_and_a_close_is_one_authorised_act()
    {
        var period = Proposal(Guid.NewGuid());

        Assert.Equal(AccountingErrorCodes.PeriodNotOpen, CodeOf(() => period.Close(AccountingWorld.Closer, AccountingWorld.ClosedAt)));
        Assert.Equal(AccountingPeriodStatus.Proposed, period.Status);

        period.Approve(AccountingWorld.Approver, AccountingWorld.ApprovedAt);
        // Ö5: an authorised close; Ö4 does not list closing, so the approver (or the preparer) may close alone.
        period.Close(AccountingWorld.Preparer, AccountingWorld.ClosedAt);
        Assert.Equal((AccountingPeriodStatus.Closed, (Guid?)AccountingWorld.Preparer), (period.Status, period.ClosedByUserId));
    }

    [Fact]
    public void Proposing_and_approving_need_an_actor_and_a_utc_instant()
    {
        var company = Guid.NewGuid();
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => AccountingPeriod.Propose(Guid.NewGuid(), company,
            September1, September30, Guid.Empty, AccountingWorld.PreparedAt)));
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => AccountingPeriod.Propose(Guid.NewGuid(), company,
            September1, September30, AccountingWorld.Preparer, new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.FromHours(3)))));
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => AccountingPeriod.Propose(Guid.Empty, company,
            September1, September30, AccountingWorld.Preparer, AccountingWorld.PreparedAt)));

        var period = Proposal(company);
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => period.Approve(Guid.Empty, AccountingWorld.ApprovedAt)));
        Assert.Equal(AccountingErrorCodes.InvalidReference, CodeOf(() => period.Approve(AccountingWorld.Approver, default)));
        Assert.Equal(AccountingPeriodStatus.Proposed, period.Status);
    }
}
