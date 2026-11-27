using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingJournalReversalDecisionWorkSourceTests
{
    [Theory] [InlineData("cancelled")] [InlineData("no-cancellation")] [InlineData("foreign-cancellation")]
    [InlineData("wrong-version")] [InlineData("wrong-invoice")] [InlineData("unposted")] [InlineData("maker-equals-actor")]
    public async Task Pending_bulk_projection_uses_exact_company_posted_voucher_cancellation_predicate(string scenario)
    {
        await using var kit = new Kit(); var detail = Detail(false); var row = PendingRow(detail);
        var sourceActor = scenario == "maker-equals-actor" ? Kit.Maker : Kit.Caller;
        var voucher = PendingVoucher(row.SourceJournalId, sourceActor);
        if (scenario == "unposted") { voucher.Status = 1; voucher.JournalId = null; voucher.ApprovedBy = null; voucher.ApprovedAtUtc = null; }
        var cancellation = PendingCancellation(voucher, scenario == "foreign-cancellation" ? Kit.OtherCompany : Kit.Company);
        if (scenario == "wrong-version") cancellation.FinalizedVersion++;
        if (scenario == "wrong-invoice") cancellation.InvoiceId = Guid.NewGuid();
        await kit.SeedAsync(Kit.Company, db =>
        {
            db.JournalReversalProposals.Add(row); db.SourceDocumentVoucherDrafts.Add(voucher);
            if (scenario is not ("no-cancellation" or "foreign-cancellation")) db.SourceInvoiceCancellations.Add(cancellation);
        });
        if (scenario == "foreign-cancellation") await kit.SeedAsync(Kit.OtherCompany, db => db.SourceInvoiceCancellations.Add(cancellation));
        var fence = kit.Fence(); var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence);
        var result = await source.ListPendingAsync(Kit.Context(), 10, default);
        var item = Assert.Single(result.Items!); Assert.False(result.HasMore); Assert.Equal(Kit.Maker.ToString("D"), item.PreparedBy);
        if (scenario == "cancelled") { Assert.Equal(Kit.CallerText, Assert.Single(item.ExcludedDeciders!)); Assert.True(item.Excludes(Kit.CallerText)); }
        else Assert.Null(item.ExcludedDeciders);
        if (scenario == "maker-equals-actor") Assert.True(item.Excludes(Kit.Maker.ToString("D"))); // Deduplicated, never unexcluded.
        Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory] [InlineData("duplicate")] [InlineData("empty-actor")] [InlineData("unrequested-journal")]
    public void Ambiguous_or_unbound_bulk_actor_facts_fail_closed_without_guessing(string fault)
    {
        var row = PendingRow(Detail(false));
        var actor = new AccountingJournalReversalWorkInboxSourceV1.PendingCancellationActor(row.SourceJournalId, Kit.Caller);
        IReadOnlyList<AccountingJournalReversalWorkInboxSourceV1.PendingCancellationActor> actors = fault switch
        {
            "duplicate" => [actor, actor], "empty-actor" => [actor with { ActorId = Guid.Empty }],
            _ => [actor with { JournalId = Guid.NewGuid() }],
        };
        var page = AccountingJournalReversalWorkInboxSourceV1.BuildPendingPage([row], actors, 1);
        Assert.Null(page.Items); Assert.False(page.HasMore);
    }

    [Fact]
    public void Bulk_actor_projection_keeps_page_count_and_deduplicates_actor_equal_to_maker()
    {
        var first = PendingRow(Detail(false)); var second = PendingRow(Detail(false)); second.Id = Guid.NewGuid();
        second.ProposedAtUtc = first.ProposedAtUtc.AddMinutes(1); second.SourceJournalId = Guid.NewGuid();
        var page = AccountingJournalReversalWorkInboxSourceV1.BuildPendingPage([first, second],
            [new(first.SourceJournalId, first.ProposedByUserId)], 1);
        var item = Assert.Single(page.Items!); Assert.True(page.HasMore); Assert.Equal(first.Id, item.ItemId);
        Assert.Null(item.ExcludedDeciders); Assert.Equal(first.ProposedByUserId.ToString("D"), item.PreparedBy);
        Assert.True(item.Excludes(first.ProposedByUserId.ToString("D")));
    }

    [Fact]
    public void Several_pending_proposals_for_one_journal_keep_their_items_and_share_one_unique_actor_fact()
    {
        var first = PendingRow(Detail(false)); var second = PendingRow(Detail(false)); second.Id = Guid.NewGuid();
        var page = AccountingJournalReversalWorkInboxSourceV1.BuildPendingPage([first, second], [new(Journal, Kit.Caller)], 2);
        Assert.False(page.HasMore); Assert.Equal(new[] { first.Id, second.Id }, page.Items!.Select(x => x.ItemId));
        Assert.All(page.Items!, item => Assert.Equal(Kit.CallerText, Assert.Single(item.ExcludedDeciders!)));
    }

    [Fact]
    public async Task Decision_and_list_use_the_same_deduplicated_KLN_actor_formatter()
    {
        await using var kit = new Kit(); var fence = kit.Fence();
        var detail = Detail(false) with { CancellationSourceActorId = Kit.Maker };
        var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence, new Owner { Detail = detail });
        var decision = (await source.ReadDecisionAsync(Kit.Context(), Proposal, default))!;
        var page = AccountingJournalReversalWorkInboxSourceV1.BuildPendingPage([PendingRow(detail)], [new(Journal, Kit.Maker)], 1);
        Assert.Null(decision.Item.ExcludedDeciders);
        Assert.True(AccountingJournalReversalDecisionBindingV1.MatchesDecisionItem(Assert.Single(page.Items!), detail));
        Assert.True(AccountingJournalReversalDecisionBindingV1.MatchesDecisionProof(decision.Item, decision.JournalReversalProof));
    }

    [Theory] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Pending_list_honors_completed_cancellation_at_every_inter_read_and_final_release_fence(int cancelAt)
    {
        await using var kit = new Kit(); await kit.SeedAsync(Kit.Company, db => db.JournalReversalProposals.Add(PendingRow(Detail(false))));
        kit.Fence(); using var cancellation = new CancellationTokenSource(); var authority = new PendingCancelAuthority(cancelAt, cancellation);
        var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db,
            new(new MutableCompany(), new MutableUser(), authority));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListPendingAsync(Kit.Context(), 1, cancellation.Token));
        Assert.Equal(cancelAt, authority.Reads); Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Pending_list_refuses_scoped_context_without_reading_companywide_items()
    {
        await using var kit = new Kit(); var authority = new Kit.FixedAuthority(true, true); var fence = kit.Fence(authority: authority);
        var source = new AccountingJournalReversalWorkInboxSourceV1(kit.Db, fence);
        var context = Kit.Context() with { GrantScope = new(new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>()) };
        var answer = await source.ListPendingAsync(context, 1, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, answer.Outcome); Assert.Null(answer.Items); Assert.Empty(authority.Asked);
        Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    private static AccountingJournalReversalProposalRow PendingRow(AccountingJournalReversalDecisionDetailV1 detail) => new()
    {
        Id = detail.Proposal.ProposalId, CompanyId = detail.Proposal.CompanyId, SourceJournalId = detail.Proposal.SourceJournalId,
        PeriodId = detail.Proposal.PeriodId, ProposedByUserId = detail.Proposal.ProposedBy, ProposedAtUtc = detail.Proposal.ProposedAtUtc,
        TransactionDate = detail.Proposal.TransactionDate, AccountingDate = detail.Proposal.AccountingDate,
        ValueDate = detail.Proposal.ValueDate, Reason = detail.Proposal.Reason, Status = 1, Version = 1,
    };
    private static AccountingSourceDocumentVoucherDraftRow PendingVoucher(Guid journal, Guid sourceActor) => new()
    {
        CompanyId = Kit.Company, Id = journal, DocumentKind = "sales-invoice", SourceDocumentId = Guid.NewGuid(), InvoiceId = Guid.NewGuid(),
        FinalizedVersion = 1, SourceActorId = sourceActor, PartyId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(),
        DocumentDate = new(2026, 11, 20), PeriodId = Period, MappingVersion = 1, Status = 2, JournalId = journal,
        ApprovedBy = sourceActor == Kit.Maker ? Kit.Caller : Kit.Maker, ApprovedAtUtc = Kit.Start.ToUniversalTime(),
        Version = 2, CreatedAtUtc = Kit.Start.ToUniversalTime(), UpdatedAtUtc = Kit.Start.ToUniversalTime(),
    };
    private static AccountingSourceInvoiceCancellationRow PendingCancellation(AccountingSourceDocumentVoucherDraftRow voucher, Guid company) => new()
    {
        CompanyId = company, CancellationId = Guid.NewGuid(), DocumentKind = voucher.DocumentKind,
        SourceDocumentId = voucher.SourceDocumentId, InvoiceId = voucher.InvoiceId, FinalizedVersion = voucher.FinalizedVersion,
        CancelledBy = Kit.Maker, CancelledAtUtc = Kit.Start.ToUniversalTime(), Reason = Kit.SyntheticReason,
        RequestHash = new string('A', 64), CorrelationId = "SENTETIK", RecordedAtUtc = Kit.Start.ToUniversalTime(),
    };
    private sealed class PendingCancelAuthority(int cancelAt, CancellationTokenSource cancellation) : IErpAuthorityRevalidationService
    {
        public int Reads;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Reads++; if (Reads == cancelAt) cancellation.Cancel(); return Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
}
