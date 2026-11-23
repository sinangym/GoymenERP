using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the normal journal drafts Accounting lists for the Work inbox, on SYNTHETIC rows. Only Draft
/// records of the server-resolved company are listed, oldest preparation first. The owner permits normal self-approval,
/// so an ordinary draft names no maker; only a credit movement draft refuses its preparer (Q9). The source writes nothing.
/// </summary>
public sealed class AccountingNormalJournalDraftWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Only_draft_records_of_the_company_are_listed_oldest_first_and_the_preparer_is_the_maker()
    {
        await using var kit = new Kit();
        var older = Draft(Kit.Start, Kit.Caller);
        var newer = Draft(Kit.Start.AddHours(1), Kit.Maker);
        var approved = Draft(Kit.Start.AddDays(-1), Kit.Maker, NormalJournalDraftStatusV1.Approved);
        var theirs = Draft(Kit.Start.AddDays(-2), Kit.Maker, company: Kit.OtherCompany);
        await kit.SeedAsync(Kit.Company, db => db.NormalJournalDrafts.AddRange(newer, older, approved));
        await kit.SeedAsync(Kit.OtherCompany, db => db.NormalJournalDrafts.Add(theirs));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Kit.AssertContractShape(result, AccountingNormalJournalDraftWorkInboxSourceV1.KindName);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        // ROL-D14: the owner refuses the preparer of every normal draft, so the caller's own draft is not theirs to decide.
        Assert.Equal(Kit.CallerText, first.PreparedBy);
        Assert.Null(first.ExcludedDeciders);
        Assert.True(first.Excludes(Kit.CallerText));
        Assert.Equal(Kit.Maker.ToString("D"), result.Items![1].PreparedBy);
        Assert.Equal(older.Id.ToString("D")[..8], first.DocumentLabel);
        Assert.Equal("/muhasebe/normal-fis-onay/" + older.Id.ToString("D"), first.Link);
        Assert.Equal(Kit.Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal([AccountingNormalJournalDraftWorkInboxSourceV1.ApprovalRequiredRule], first.Rules.Select(rule => rule.RuleCode));
        Assert.Equal(["source-record", "currency"], first.References.Select(reference => reference.Role));
        Assert.False(kit.Db.ChangeTracker.HasChanges());

        var other = await Source(kit, Kit.OtherCompany).ListPendingAsync(Kit.Context(Kit.OtherCompany), 10, CancellationToken.None);
        Assert.Equal([theirs.Id], other.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task A_credit_movement_draft_names_its_preparer_as_maker_because_the_owner_refuses_credit_self_approval()
    {
        await using var kit = new Kit();
        var credit = Draft(Kit.Start, Kit.Maker);
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.NormalJournalDrafts.Add(credit);
            db.CreditMovementProposals.Add(new CreditMovementProposalRow
            {
                Id = credit.SourceId,
                CompanyId = Kit.Company,
                MovementId = Guid.NewGuid(),
                SourceHash = new string('b', 64),
                SourceJson = "{}",
                MappingStatus = "SENTETIK",
                WaitingReasonsJson = "[]",
                ReceivedAtUtc = Kit.Start.ToUniversalTime(),
            });
        });

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        var item = Assert.Single(result.Items!);
        Assert.Equal(Kit.Maker.ToString("D"), item.PreparedBy);
        Assert.True(item.Excludes(Kit.Maker.ToString("D").ToUpperInvariant()));
        Assert.Equal(
            [AccountingNormalJournalDraftWorkInboxSourceV1.ApprovalRequiredRule, AccountingNormalJournalDraftWorkInboxSourceV1.CreditMakerCheckerRule],
            item.Rules.Select(rule => rule.RuleCode));
        Assert.Equal("credit-movement-proposal", item.References[0].Role);
        Assert.Equal(credit.SourceId, item.References[0].RecordId);
    }

    [Fact]
    public async Task More_drafts_than_asked_set_has_more_and_a_broken_draft_makes_the_source_unavailable()
    {
        await using var kit = new Kit();
        var rows = Enumerable.Range(0, 3).Select(index => Draft(Kit.Start.AddMinutes(index), Kit.Maker)).ToArray();
        await kit.SeedAsync(Kit.Company, db => db.NormalJournalDrafts.AddRange(rows));

        var page = await Source(kit).ListPendingAsync(Kit.Context(), 2, CancellationToken.None);
        Assert.True(page.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), page.Items!.Select(item => item.ItemId));

        var broken = Draft(Kit.Start.AddMinutes(-1), Kit.Maker);
        broken.PostedJournalId = Guid.NewGuid();
        await kit.SeedAsync(Kit.Company, db => db.NormalJournalDrafts.Add(broken));
        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static AccountingNormalJournalDraftWorkInboxSourceV1 Source(Kit kit, Guid? company = null)
    {
        var fence = kit.Fence(company);
        return new AccountingNormalJournalDraftWorkInboxSourceV1(kit.Db, fence);
    }

    private static NormalJournalDraftRow Draft(DateTimeOffset at, Guid preparedBy,
        NormalJournalDraftStatusV1 status = NormalJournalDraftStatusV1.Draft, Guid? company = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company ?? Kit.Company,
            Status = (int)status,
            Version = 1,
            ContentHash = new string('c', 64),
            PreparedByUserId = preparedBy,
            PreparedAtUtc = at.ToUniversalTime(),
            ApprovedByUserId = status == NormalJournalDraftStatusV1.Draft ? null : Kit.Caller,
            ApprovedAtUtc = status == NormalJournalDraftStatusV1.Draft ? null : at.ToUniversalTime(),
            PostedJournalId = status == NormalJournalDraftStatusV1.Draft ? null : Guid.NewGuid(),
            Consumer = "SENTETIK-consumer",
            MessageId = "SENTETIK-" + Guid.NewGuid().ToString("N"),
            SourceId = Guid.NewGuid(),
            PostingPurpose = "SENTETIK-purpose",
            PostingRuleVersion = "1",
            CurrencyId = Guid.NewGuid(),
            TransactionDate = new DateOnly(2026, 11, 20),
            AccountingDate = new DateOnly(2026, 11, 20),
            RecordedAtUtc = at.ToUniversalTime(),
            AccountPlanSourceVersion = "SENTETIK-plan",
            AccountPlanSourceSha256 = new string('d', 64),
            AccountPlanApprovedByUserId = Kit.Maker,
            AccountPlanApprovedAtUtc = at.ToUniversalTime(),
            PostingRuleApprovedByUserId = Kit.Maker,
            PostingRuleApprovedAtUtc = at.ToUniversalTime(),
            CreatedAtUtc = at.ToUniversalTime(),
            UpdatedAtUtc = at.ToUniversalTime(),
        };
}

/// <summary>
/// W1-A (AI-D31, AI-D32): the journal reversal proposals Accounting lists for the Work inbox, on SYNTHETIC rows. Only
/// Pending proposals of the server-resolved company are listed, oldest first, the caller's own included; the proposer is
/// the maker (maker ≠ checker). The source writes nothing.
/// </summary>
public sealed class AccountingJournalReversalWorkInboxSourceV1Tests
{
    [Fact]
    public async Task Only_pending_proposals_of_the_company_are_listed_oldest_first_with_the_proposer_as_maker()
    {
        await using var kit = new Kit();
        var older = Proposal(Kit.Start, Kit.Caller);
        var newer = Proposal(Kit.Start.AddHours(1), Kit.Maker);
        var theirs = Proposal(Kit.Start.AddDays(-2), Kit.Maker, company: Kit.OtherCompany);
        await kit.SeedAsync(Kit.Company, db => db.JournalReversalProposals.AddRange(newer, older,
            Proposal(Kit.Start.AddDays(-1), Kit.Maker, AccountingJournalReversalProposalStatusV1.Approved),
            Proposal(Kit.Start.AddDays(-1), Kit.Maker, AccountingJournalReversalProposalStatusV1.Rejected)));
        await kit.SeedAsync(Kit.OtherCompany, db => db.JournalReversalProposals.Add(theirs));

        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Kit.AssertContractShape(result, AccountingJournalReversalWorkInboxSourceV1.KindName);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(Kit.CallerText, first.PreparedBy);
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal("/muhasebe/ters-kayit-onay/" + older.Id.ToString("D"), first.Link);
        Assert.Equal(Kit.Start.ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal([AccountingJournalReversalWorkInboxSourceV1.ApprovalRequiredRule], first.Rules.Select(rule => rule.RuleCode));
        Assert.Equal(new WorkApprovalReferenceV1("journal", older.SourceJournalId.ToString("D")[..8], older.SourceJournalId,
            "/muhasebe/fisler/" + older.SourceJournalId.ToString("D")), first.References[0]);
        Assert.Equal(("period", older.PeriodId), (first.References[1].Role, first.References[1].RecordId));
        Assert.False(kit.Db.ChangeTracker.HasChanges());

        var other = await Source(kit, Kit.OtherCompany).ListPendingAsync(Kit.Context(Kit.OtherCompany), 10, CancellationToken.None);
        Assert.Equal([theirs.Id], other.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task More_proposals_than_asked_set_has_more_and_a_broken_proposal_makes_the_source_unavailable()
    {
        await using var kit = new Kit();
        var rows = Enumerable.Range(0, 3).Select(index => Proposal(Kit.Start.AddMinutes(index), Kit.Maker)).ToArray();
        await kit.SeedAsync(Kit.Company, db => db.JournalReversalProposals.AddRange(rows));

        var page = await Source(kit).ListPendingAsync(Kit.Context(), 2, CancellationToken.None);
        Assert.True(page.HasMore);
        Assert.Equal(rows.Take(2).Select(row => row.Id), page.Items!.Select(item => item.ItemId));

        var broken = Proposal(Kit.Start.AddMinutes(-1), Kit.Maker);
        broken.DecidedByUserId = Kit.Caller;
        await kit.SeedAsync(Kit.Company, db => db.JournalReversalProposals.Add(broken));
        var result = await Source(kit).ListPendingAsync(Kit.Context(), 10, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static AccountingJournalReversalWorkInboxSourceV1 Source(Kit kit, Guid? company = null)
    {
        var fence = kit.Fence(company);
        return new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence);
    }

    private static AccountingJournalReversalProposalRow Proposal(DateTimeOffset at, Guid proposedBy,
        AccountingJournalReversalProposalStatusV1 status = AccountingJournalReversalProposalStatusV1.Pending, Guid? company = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company ?? Kit.Company,
            SourceJournalId = Guid.NewGuid(),
            ReversalJournalId = status == AccountingJournalReversalProposalStatusV1.Approved ? Guid.NewGuid() : null,
            PeriodId = Guid.NewGuid(),
            TransactionDate = new DateOnly(2026, 11, 20),
            AccountingDate = new DateOnly(2026, 11, 20),
            Reason = Kit.SyntheticReason,
            ProposedByUserId = proposedBy,
            ProposedAtUtc = at.ToUniversalTime(),
            Status = (int)status,
            DecidedByUserId = status == AccountingJournalReversalProposalStatusV1.Pending ? null : Kit.Caller,
            DecidedAtUtc = status == AccountingJournalReversalProposalStatusV1.Pending ? null : at.ToUniversalTime(),
            Version = status == AccountingJournalReversalProposalStatusV1.Pending ? 1 : 2,
        };
}
