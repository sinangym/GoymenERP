using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the period opening approvals (Ö4) Accounting lists for the Work inbox, on SYNTHETIC rows. Only
/// Proposed periods of the server-resolved company are listed, oldest preparation first, the caller's own included (Work
/// hides a maker's own item); the preparer is the maker. The source writes nothing.
/// </summary>
public sealed class AccountingPeriodOpeningWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Only_proposed_periods_of_the_company_are_listed_oldest_first_with_the_preparer_and_the_callers_own_included()
    {
        await using var kit = new Kit();
        var older = Period(new DateOnly(2026, 12, 1), Kit.Start, Kit.Caller);
        var newer = Period(new DateOnly(2027, 1, 1), Kit.Start.AddHours(1), Kit.Maker);
        var theirs = Period(new DateOnly(2027, 2, 1), Kit.Start.AddDays(-1), Kit.Maker, company: Kit.OtherCompany);
        await kit.SeedAsync(Kit.Company, db => db.Periods.AddRange(newer, older,
            Period(new DateOnly(2026, 10, 1), Kit.Start.AddDays(-3), Kit.Maker, AccountingPeriodStatus.Open),
            Period(new DateOnly(2026, 9, 1), Kit.Start.AddDays(-4), Kit.Maker, AccountingPeriodStatus.Closed)));
        await kit.SeedAsync(Kit.OtherCompany, db => db.Periods.Add(theirs));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Kit.AssertContractShape(result, AccountingPeriodOpeningWorkInboxSourceV1.KindName);
        Assert.Equal(Kit.Company, result.CompanyId);
        Assert.False(result.HasMore);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(Kit.CallerText, first.PreparedBy);
        Assert.True(first.Excludes(Kit.CallerText));
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal("2026-12-01 / 2026-12-31", first.DocumentLabel);
        Assert.Equal(Kit.Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal(AccountingPeriodOpeningWorkInboxSourceV1.ScreenPath, first.Link);
        Assert.Equal([new WorkApprovalRuleResultV1(AccountingPeriodOpeningWorkInboxSourceV1.OpeningApprovalRequiredRule,
            null, null, null, null)], first.Rules);
        Assert.Equal(new WorkApprovalReferenceV1("period", "2026-12-01 / 2026-12-31", older.Id, "/muhasebe/donemler"),
            Assert.Single(first.References));
        Assert.False(kit.Db.ChangeTracker.HasChanges());

        var other = await Source(kit, Kit.OtherCompany).ListPendingAsync(Kit.Context(Kit.OtherCompany), 10, CancellationToken.None);
        Assert.Equal([theirs.Id], other.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task More_proposals_than_asked_set_has_more_and_return_the_oldest()
    {
        await using var kit = new Kit();
        var rows = Enumerable.Range(0, 3)
            .Select(index => Period(new DateOnly(2027, index + 1, 1), Kit.Start.AddMinutes(index), Kit.Maker)).ToArray();
        await kit.SeedAsync(Kit.Company, db => db.Periods.AddRange(rows));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task A_proposal_that_breaks_the_owner_invariants_makes_the_source_unavailable_never_a_partial_list()
    {
        await using var kit = new Kit();
        var broken = Period(new DateOnly(2027, 1, 1), Kit.Start.AddMinutes(1), Kit.Maker);
        broken.ApprovedByUserId = Kit.Caller;
        await kit.SeedAsync(Kit.Company, db => db.Periods.AddRange(
            Period(new DateOnly(2026, 12, 1), Kit.Start, Kit.Maker), broken));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Items);
    }

    private static AccountingPeriodOpeningWorkInboxSourceV1 Source(Kit kit, Guid? company = null)
    {
        var fence = kit.Fence(company);
        return new AccountingPeriodOpeningWorkInboxSourceV1(kit.Db, fence);
    }

    internal static PeriodRow Period(DateOnly startsOn, DateTimeOffset preparedAt, Guid preparedBy,
        AccountingPeriodStatus status = AccountingPeriodStatus.Proposed, Guid? company = null, long version = 1) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company ?? Kit.Company,
            StartsOn = startsOn,
            EndsOn = startsOn.AddMonths(1).AddDays(-1),
            Status = (int)status,
            PreparedByUserId = preparedBy,
            PreparedAtUtc = preparedAt.ToUniversalTime(),
            ApprovedByUserId = status == AccountingPeriodStatus.Proposed ? null : Kit.Caller,
            ApprovedAtUtc = status == AccountingPeriodStatus.Proposed ? null : preparedAt.ToUniversalTime(),
            ClosedByUserId = status == AccountingPeriodStatus.Closed ? Kit.Caller : null,
            ClosedAtUtc = status == AccountingPeriodStatus.Closed ? preparedAt.ToUniversalTime() : null,
            Version = version,
            CreatedAtUtc = preparedAt.ToUniversalTime(),
            UpdatedAtUtc = preparedAt.ToUniversalTime(),
        };
}

/// <summary>
/// W1-A (AI-D31, AI-D32): the period reopening proposals (FNS-D42, ROL-D12) Accounting lists for the Work inbox, on
/// SYNTHETIC rows. A proposal waits only while it is unapproved and its close is still the period's current close; the
/// proposer is the maker; a still closed later period is shown as a rule (approval would be refused). The source writes
/// nothing.
/// </summary>
public sealed class AccountingPeriodReopeningWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Only_unapproved_proposals_on_the_current_close_are_listed_oldest_first_with_the_proposer_as_maker()
    {
        await using var kit = new Kit();
        var (period, close) = Closed(new DateOnly(2026, 10, 1), version: 3);
        var older = Proposal(period, close, Kit.Start, Kit.Caller);
        var newer = Proposal(period, close, Kit.Start.AddHours(1), Kit.Maker);
        var approved = Proposal(period, close, Kit.Start.AddMinutes(-5), Kit.Maker);
        approved.ApprovedBy = Kit.Caller;
        approved.ApprovedAtUtc = Kit.Start.ToUniversalTime();
        approved.Version = 2;
        // A proposal on an older close of the same period (that close was already reopened) is not approvable.
        var (_, oldClose) = Closed(new DateOnly(2026, 10, 1), version: 1, periodId: period.Id);
        oldClose.ReopeningId = Guid.NewGuid();
        var stale = Proposal(period, oldClose, Kit.Start.AddMinutes(-10), Kit.Maker);
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.Periods.Add(period);
            db.PeriodClosures.AddRange(close, oldClose);
            db.PeriodReopenings.AddRange(newer, older, approved, stale);
        });
        var (theirPeriod, theirClose) = Closed(new DateOnly(2026, 10, 1), version: 2, company: Kit.OtherCompany);
        await kit.SeedAsync(Kit.OtherCompany, db =>
        {
            db.Periods.Add(theirPeriod);
            db.PeriodClosures.Add(theirClose);
            db.PeriodReopenings.Add(Proposal(theirPeriod, theirClose, Kit.Start.AddDays(-1), Kit.Maker));
        });

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Kit.AssertContractShape(result, AccountingPeriodReopeningWorkInboxSourceV1.KindName);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(Kit.CallerText, first.PreparedBy);
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal("2026-10-01 / 2026-10-31", first.DocumentLabel);
        Assert.Equal(Kit.Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal(AccountingPeriodReopeningWorkInboxSourceV1.ScreenPath, first.Link);
        Assert.Equal([new WorkApprovalRuleResultV1(AccountingPeriodReopeningWorkInboxSourceV1.ReopeningRequestedRule,
            null, null, null, null)], first.Rules);
        Assert.Equal(["period", "period-closure"], first.References.Select(reference => reference.Role));
        Assert.Equal(period.Id, first.References[0].RecordId);
        Assert.Equal(close.Id, first.References[1].RecordId);
        Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task A_later_closed_period_is_shown_as_a_rule_because_the_owner_would_refuse_until_it_is_reopened()
    {
        await using var kit = new Kit();
        var (period, close) = Closed(new DateOnly(2026, 10, 1), version: 3);
        var (later, laterClose) = Closed(new DateOnly(2026, 11, 1), version: 3);
        var proposal = Proposal(period, close, Kit.Start, Kit.Maker);
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.Periods.AddRange(period, later);
            db.PeriodClosures.AddRange(close, laterClose);
            db.PeriodReopenings.Add(proposal);
        });

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        var item = Assert.Single(result.Items!);
        Assert.Equal(
            [AccountingPeriodReopeningWorkInboxSourceV1.ReopeningRequestedRule, AccountingPeriodReopeningWorkInboxSourceV1.LaterPeriodClosedRule],
            item.Rules.Select(rule => rule.RuleCode));
    }

    [Fact]
    public async Task More_proposals_than_asked_set_has_more_and_a_broken_proposal_makes_the_source_unavailable()
    {
        await using var kit = new Kit();
        var (period, close) = Closed(new DateOnly(2026, 10, 1), version: 3);
        var rows = Enumerable.Range(0, 3).Select(index => Proposal(period, close, Kit.Start.AddMinutes(index), Kit.Maker)).ToArray();
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.Periods.Add(period);
            db.PeriodClosures.Add(close);
            db.PeriodReopenings.AddRange(rows);
        });

        var page = await Source(kit).ListPendingAsync(Kit.Context(), 2, CancellationToken.None);
        Assert.True(page.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), page.Items!.Select(item => item.ItemId));

        var broken = Proposal(period, close, Kit.Start.AddMinutes(-1), Guid.Empty);
        await kit.SeedAsync(Kit.Company, db => db.PeriodReopenings.Add(broken));
        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Items);
    }

    private static AccountingPeriodReopeningWorkInboxSourceV1 Source(Kit kit)
    {
        var fence = kit.Fence();
        return new AccountingPeriodReopeningWorkInboxSourceV1(kit.Db, fence);
    }

    private static (PeriodRow Period, AccountingPeriodClosureRow Close) Closed(DateOnly startsOn, long version,
        Guid? company = null, Guid? periodId = null)
    {
        var period = AccountingPeriodOpeningWorkInboxSourceV1Tests.Period(startsOn, Kit.Start.AddDays(-30), Kit.Maker,
            AccountingPeriodStatus.Closed, company, version);
        if (periodId is { } id)
        {
            period.Id = id;
        }

        var close = new AccountingPeriodClosureRow
        {
            Id = Guid.NewGuid(),
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            ClosedPeriodVersion = version,
            TargetPeriodId = Guid.NewGuid(),
            CarryVoucherId = Guid.NewGuid(),
            SourceHash = new string('a', 64),
            LineCount = 1,
            ClosedBy = Kit.Maker,
            ClosedAtUtc = Kit.Start.AddDays(-2).ToUniversalTime(),
            Version = 1,
        };
        return (period, close);
    }

    private static AccountingPeriodReopeningRow Proposal(PeriodRow period, AccountingPeriodClosureRow close,
        DateTimeOffset at, Guid proposedBy) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            ClosureId = close.Id,
            ClosedPeriodVersion = close.ClosedPeriodVersion,
            ProposedBy = proposedBy,
            ProposedAtUtc = at.ToUniversalTime(),
            Reason = Kit.SyntheticReason,
            Version = 1,
        };
}
