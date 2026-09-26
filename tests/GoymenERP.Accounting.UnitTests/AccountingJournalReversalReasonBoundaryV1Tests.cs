using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingJournalReversalReasonBoundaryV1Tests
{
    private static readonly Guid Company = Guid.Parse("d1000000-0000-4000-8000-000000000001");
    private static readonly Guid Actor = Guid.Parse("d1000000-0000-4000-8000-000000000002");

    [Fact]
    public void Reason_at_the_persisted_nvarchar_boundary_is_accepted_losslessly()
    {
        var reason = new string('x', AccountingJournalReversalPolicyV1.ReasonMaximumLength);

        var proposal = AccountingJournalReversalPolicyV1.Propose(
            Company, Actor, Request(reason), DateTimeOffset.Parse("2026-09-25T15:00:00Z"));

        Assert.Equal(AccountingJournalReversalPolicyV1.ReasonMaximumLength, proposal.Reason.Length);
        Assert.Equal(reason, proposal.Reason);
    }

    [Fact]
    public void Reason_over_the_persisted_nvarchar_boundary_is_refused_before_the_store()
    {
        var reason = new string('x', AccountingJournalReversalPolicyV1.ReasonMaximumLength + 1);

        var exception = Assert.Throws<AccountingJournalReversalPolicyExceptionV1>(() =>
            AccountingJournalReversalPolicyV1.Propose(
                Company, Actor, Request(reason), DateTimeOffset.Parse("2026-09-25T15:00:00Z")));

        Assert.Equal(AccountingJournalReversalPolicyCodesV1.InvalidRequest, exception.Code);
        Assert.Equal("reason", exception.Field);
    }

    private static ProposeAccountingJournalReversalV1 Request(string reason) => new(
        Guid.Parse("d1000000-0000-4000-8000-000000000003"),
        Guid.Parse("d1000000-0000-4000-8000-000000000004"),
        Guid.Parse("d1000000-0000-4000-8000-000000000005"),
        new DateOnly(2026, 9, 25),
        new DateOnly(2026, 9, 25),
        null,
        reason);
}
