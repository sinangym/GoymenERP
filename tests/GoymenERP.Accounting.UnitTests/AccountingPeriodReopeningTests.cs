using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Periods;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingPeriodReopeningTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reopening_keeps_original_opening_approval_and_requires_different_checker()
    {
        var period = Closed(); var openingApprover = period.ApprovedByUserId;
        var maker = Guid.NewGuid(); var checker = Guid.NewGuid();
        var proposal = AccountingPeriodReopening.Propose(Guid.NewGuid(), period, Guid.NewGuid(), 3, maker, At, "SENTETIK düzeltme");
        Assert.Equal(AccountingErrorCodes.PeriodSelfApproval,
            Assert.Throws<AccountingDomainException>(() => proposal.Approve(maker, At)).Code);
        Assert.Throws<AccountingDomainException>(() => period.Reopen(proposal));
        var approved = proposal.Approve(checker, At.AddMinutes(1));
        period.Reopen(approved);
        Assert.Equal(AccountingPeriodStatus.Open, period.Status);
        Assert.Equal(openingApprover, period.ApprovedByUserId);
        Assert.Null(period.ClosedByUserId); Assert.Null(period.ClosedAtUtc);
        Assert.Equal(maker, approved.ProposedBy); Assert.Equal(checker, approved.ApprovedBy);
        Assert.Equal(2, approved.Version); Assert.Equal(1, proposal.Version);
        Assert.Throws<AccountingDomainException>(() => approved.Approve(Guid.NewGuid(), At.AddMinutes(2)));
        Assert.Throws<AccountingDomainException>(() => period.Reopen(approved));
    }

    [Theory]
    [InlineData("not-closed")]
    [InlineData("no-closure")]
    [InlineData("no-version")]
    [InlineData("no-actor")]
    [InlineData("no-reason")]
    [InlineData("non-utc")]
    public void Reopening_never_fills_missing_closure_actor_or_decision_facts(string fault)
    {
        var period = Closed();
        if (fault == "not-closed") period = AccountingPeriod.Propose(Guid.NewGuid(), Guid.NewGuid(),
            new(2026, 11, 1), new(2026, 11, 30), Guid.NewGuid(), At);
        Assert.Throws<AccountingDomainException>(() => AccountingPeriodReopening.Propose(Guid.NewGuid(), period,
            fault == "no-closure" ? Guid.Empty : Guid.NewGuid(), fault == "no-version" ? 0 : 3,
            fault == "no-actor" ? Guid.Empty : Guid.NewGuid(), fault == "non-utc" ? At.ToOffset(TimeSpan.FromHours(3)) : At,
            fault == "no-reason" ? " " : "SENTETIK düzeltme"));
    }

    [Fact]
    public void Approval_of_another_period_cannot_reopen_this_period()
    {
        var period = Closed(); var other = Closed();
        var approved = AccountingPeriodReopening.Propose(Guid.NewGuid(), other, Guid.NewGuid(), 3,
            Guid.NewGuid(), At, "SENTETIK").Approve(Guid.NewGuid(), At.AddMinutes(1));
        Assert.Throws<AccountingDomainException>(() => period.Reopen(approved));
        Assert.Equal(AccountingPeriodStatus.Closed, period.Status);
    }

    private static AccountingPeriod Closed()
    {
        var period = AccountingPeriod.Propose(Guid.NewGuid(), Guid.NewGuid(), new(2026, 11, 1), new(2026, 11, 30),
            Guid.NewGuid(), At.AddDays(-2));
        period.Approve(Guid.NewGuid(), At.AddDays(-1)); period.Close(Guid.NewGuid(), At);
        return period;
    }
}
