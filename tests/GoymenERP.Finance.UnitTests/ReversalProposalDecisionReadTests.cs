using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using System.Text.Json;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

// SENTETIK read projection/fences only; this fixture is not real SQL, Audit, treasury or Identity acceptance.
public sealed class ReversalProposalDecisionReadTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approver_only_read_discloses_original_facts_without_general_receipt_or_bank_permission_or_writes()
    {
        await using var fixture = await Fixture.StartAsync(); var calls = 0;
        var answer = await fixture.Core.ReadDecisionAsync(fixture.Context, fixture.Proposal.Id, _ => { calls++; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Found, answer.Outcome); Assert.Equal(10, calls);
        var detail = Assert.IsType<ReceiptReversalDecisionDetailV1>(answer.Resource);
        Assert.Equal(Company, detail.CompanyId); Assert.Equal(fixture.Proposal.Id, detail.Proposal.Id);
        Assert.Equal("001250.50", detail.OriginalReceipt.AmountText); Assert.Equal("2", detail.OriginalReceipt.SourceVersion);
        Assert.Equal(fixture.Original.Id, detail.OriginalReceipt.SourceId); Assert.Null(detail.OriginalReceipt.PosCommissionBearer);
        Assert.All(fixture.Permissions.Actions, action => Assert.Equal(FinanceAction.ApproveReversal, action));
        Assert.False(fixture.Database.ChangeTracker.HasChanges()); Assert.Empty(await fixture.Database.Commands.ToListAsync());
        Assert.Empty(fixture.Collector.Pending);
        var changedBody = detail with { OriginalReceipt = detail.OriginalReceipt with { AmountText = "1250.5" } };
        Assert.NotEqual(ReversalProposalService.DecisionPreviewDigest(detail), ReversalProposalService.DecisionPreviewDigest(changedBody));
    }

    [Fact]
    public async Task Work_source_reuses_guarded_owner_and_only_serializes_existing_inbox_fields_not_human_reason_or_proof()
    {
        await using var fixture = await Fixture.StartAsync();
        var authority = new FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority(true, true);
        var fence = new FinanceWorkInboxFence(fixture.Company, fixture.User, authority);
        var source = new FinanceManualReceiptReversalWorkInboxSourceV1(fixture.Database, fence, fixture.Core.WithApprovalSafety());
        var snapshot = await source.ReadDecisionAsync(new(Company, "checker", "SENTETIK-source-trace", "SENTETIK-source-cause"), fixture.Proposal.Id, default);
        Assert.All(fixture.Permissions.Contexts, c => { Assert.Equal("SENTETIK-source-trace", c.CorrelationId); Assert.Equal("SENTETIK-source-cause", c.CausationId); });
        Assert.NotEmpty(fixture.Permissions.Contexts);
        Assert.NotNull(snapshot); Assert.True(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion);
        var human = (await fixture.Core.ReadDecisionAsync(fixture.Context, fixture.Proposal.Id, _ => Task.CompletedTask, default)).Resource!;
        Assert.True(ReversalProposalService.MatchesDecisionSnapshot(human, snapshot));
        Assert.True(ReversalProposalService.MatchesDecisionItem(snapshot.Item, human));
        var legacy = await source.ListPendingAsync(new(Company, "checker", "SENTETIK-source-trace", "SENTETIK-source-cause"), 10, default);
        Assert.True(ReversalProposalService.MatchesDecisionItem(Assert.Single(legacy.Items!), human));
        Assert.Equal("SENTETIK named bank", human.TreasuryAccountLabel);
        Assert.Contains(snapshot.Item.References, r => r.Role == "bank-account" && r.Label == human.TreasuryAccountLabel);
        var renamed = human with { TreasuryAccountLabel = "SENTETIK different label" };
        Assert.NotEqual(ReversalProposalService.DecisionPreviewDigest(human), ReversalProposalService.DecisionPreviewDigest(renamed));
        Assert.Equal(ReversalProposalService.RecordedFactsDigest(human), ReversalProposalService.RecordedFactsDigest(renamed));
        Assert.False(ReversalProposalService.MatchesDecisionItem(snapshot.Item, renamed));
        Assert.False(ReversalProposalService.MatchesDecisionItem(snapshot.Item with { DocumentLabel = "different" }, human));
        Assert.False(ReversalProposalService.MatchesDecisionItem(snapshot.Item with { Link = "/wrong" }, human));
        Assert.False(ReversalProposalService.MatchesDecisionItem(snapshot.Item with { ExcludedDeciders = ["other"] }, human));
        Assert.False(ReversalProposalService.MatchesDecisionSnapshot(human, snapshot with { SourceVersion = "2" }));
        Assert.False(ReversalProposalService.MatchesDecisionSnapshot(human, snapshot with { ManualReceiptReversalProof = snapshot.ManualReceiptReversalProof! with { RecordedFactsDigest = new string('f', 64) } }));
        Assert.False(ReversalProposalService.MatchesDecisionSnapshot(human, snapshot with { Item = snapshot.Item with { References = snapshot.Item.References.Select(r => r.Role == "manual-receipt" ? r with { RecordId = Guid.NewGuid() } : r).ToArray() } }));
        var wire = JsonSerializer.Serialize(snapshot, FinanceJson.CreateOptions());
        Assert.DoesNotContain("manualReceiptReversalProof", wire); Assert.DoesNotContain("recordedFactsDigest", wire);
        Assert.DoesNotContain("SENTETIK reason", wire); Assert.Equal($"/onaylarim/finance.manual-receipt-reversal.v1/{fixture.Proposal.Id:D}", snapshot.Item.Link);
        Assert.All(authority.Asked, query => Assert.Equal(FinancePermissionNames.ReverseApprove, query.Permission));
        var missingOwner = new FinanceManualReceiptReversalWorkInboxSourceV1(fixture.Database, fence);
        await Assert.ThrowsAsync<InvalidOperationException>(() => missingOwner.ReadDecisionAsync(new(Company, "checker", "SENTETIK-source-trace", "SENTETIK-source-cause"), fixture.Proposal.Id, default));
    }

    [Theory]
    [InlineData("propose-only")] [InlineData("maker")] [InlineData("foreign-company")]
    [InlineData("actor-loss")] [InlineData("company-loss")] [InlineData("permission-loss")]
    public async Task Purpose_read_refuses_missing_approve_own_maker_and_late_context_or_authority_loss(string failure)
    {
        await using var fixture = await Fixture.StartAsync(); var context = fixture.Context;
        if (failure == "propose-only")
        {
            fixture.Permissions.ProposeOnly = true;
            Assert.Equal(ReadOutcome.Found, (await fixture.Core.DetailAuthorizedAsync(context, fixture.Proposal.Id, default)).Outcome);
        }
        if (failure == "maker") { fixture.User.Actor = "maker"; context = context with { ActorId = "maker" }; }
        if (failure == "foreign-company") context = context with { CompanyId = Guid.NewGuid() };
        var calls = 0;
        var answer = await fixture.Core.ReadDecisionAsync(context, fixture.Proposal.Id, _ =>
        {
            if (++calls == 6)
            {
                if (failure == "actor-loss") fixture.User.Actor = "other";
                if (failure == "company-loss") fixture.Company.Set(Guid.NewGuid());
                if (failure == "permission-loss") fixture.Permissions.Allowed = false;
            }
            return Task.CompletedTask;
        }, default);
        Assert.Equal(ReadOutcome.Denied, answer.Outcome); Assert.Null(answer.Resource);
        Assert.False(fixture.Database.ChangeTracker.HasChanges()); Assert.Empty(await fixture.Database.Commands.ToListAsync());
    }

    [Fact]
    public async Task Company_bound_bank_label_read_is_followed_by_fresh_authority_before_preview_release()
    {
        await using var fixture = await Fixture.StartAsync(); var calls = 0;
        var answer = await fixture.Core.ReadDecisionAsync(fixture.Context, fixture.Proposal.Id, _ =>
        {
            // Callback 7 is the first consumer fence immediately after the added bank-label query.
            if (++calls == 7) fixture.Permissions.Allowed = false;
            return Task.CompletedTask;
        }, default);
        Assert.Equal(ReadOutcome.Denied, answer.Outcome); Assert.Null(answer.Resource); Assert.Equal(8, calls);
        Assert.False(fixture.Database.ChangeTracker.HasChanges()); Assert.Empty(await fixture.Database.Commands.ToArrayAsync());
    }

    [Fact]
    public async Task Native_decision_between_original_read_and_final_version_fence_cannot_create_a_mixed_preview()
    {
        await using var fixture = await Fixture.StartAsync(); var calls = 0; var changed = false;
        var answer = await fixture.Core.ReadDecisionAsync(fixture.Context, fixture.Proposal.Id, async _ =>
        {
            if (++calls != 6) return;
            await using var writer = fixture.NewDatabase();
            var native = fixture.Service(writer, new AuditEventCollector());
            var rejected = await native.RejectAsync(fixture.Context, new("SENTETIK-concurrent-key"), new(fixture.Proposal.Id, "1", "SENTETIK ret"), default);
            Assert.Equal(CommandOutcome.Succeeded, rejected.Outcome); changed = true;
        }, default);
        Assert.True(changed); Assert.Equal(ReadOutcome.Unavailable, answer.Outcome); Assert.Null(answer.Resource);
        Assert.False(fixture.Database.ChangeTracker.HasChanges());
        Assert.Equal(ReversalProposalStatuses.Rejected, (await fixture.Database.ReversalProposals.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Native_clock_rollback_decision_is_read_without_inventing_a_timestamp_order_policy(bool approve)
    {
        await using var fixture = await Fixture.StartAsync(); var decidedAt = At.AddMinutes(-1);
        // Real native policy and owner model, synthetic in-memory persistence; not guarded SQL commit acceptance.
        await using (var writer = fixture.NewDatabase())
        {
            var proposal = await writer.ReversalProposals.SingleAsync();
            var original = await writer.ReceiptDrafts.AsNoTracking().SingleAsync();
            if (approve)
            {
                var rows = ReversalProposalPolicy.Approve(original, proposal, new(proposal.Id, "1"), Guid.NewGuid(), Guid.NewGuid(),
                    "checker", "SENTETIK-clock-rollback", decidedAt);
                writer.ReceiptDrafts.Add(rows.Source); writer.MoneyEvents.Add(rows.Event);
            }
            else ReversalProposalPolicy.Reject(original, proposal, new(proposal.Id, "1", "SENTETIK ret"), "checker", decidedAt);
            await writer.SaveChangesAsync();
        }
        var result = await fixture.Core.ReadDecisionAsync(fixture.Context, fixture.Proposal.Id, _ => Task.CompletedTask, default);
        Assert.Equal(ReadOutcome.Found, result.Outcome); Assert.Equal(decidedAt, result.Resource!.Proposal.DecidedAtUtc);
        Assert.Equal(At, result.Resource.Proposal.ProposedAtUtc); Assert.Equal("2", result.Resource.Proposal.Version);
        Assert.Equal(approve ? ReversalProposalStatus.Approved : ReversalProposalStatus.Rejected, result.Resource.Proposal.Status);
        Assert.Empty(await fixture.Database.Commands.ToArrayAsync()); Assert.False(fixture.Database.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("company")] [InlineData("source")] [InlineData("kind")] [InlineData("amount")]
    [InlineData("party")] [InlineData("bank")] [InlineData("currency")] [InlineData("profile")]
    [InlineData("recorded-time")] [InlineData("pos-partial")]
    public void Original_human_facts_require_one_matching_immutable_original_money_event(string failure)
    {
        var (source, fact) = Original();
        switch (failure)
        {
            case "company": fact.CompanyId = Guid.NewGuid(); break;
            case "source": fact.SourceId = Guid.NewGuid(); break;
            case "kind": fact.Kind = MoneyEventKinds.ManualBankReceiptReversed; break;
            case "amount": fact.AmountText = "1250.50"; break;
            case "party": fact.PartyId = Guid.NewGuid(); break;
            case "bank": fact.TreasuryAccountId = Guid.NewGuid(); break;
            case "currency": fact.CurrencyId = Guid.NewGuid(); break;
            case "profile": fact.ProfileVersion = "other"; break;
            case "recorded-time": fact.RecordedAtUtc = At.AddSeconds(1); break;
            case "pos-partial": source.PosCommissionBearer = fact.PosCommissionBearer = PosCommissionBearers.Company; break;
        }
        Assert.Null(ReversalProposalService.OriginalReceipt(Company, source.Id, source, fact));
        Assert.Null(ReversalProposalService.OriginalReceipt(Company, source.Id, source, null));
    }

    [Theory]
    [InlineData(PosCommissionBearer.Company)] [InlineData(PosCommissionBearer.Customer)]
    public void Retained_POS_facts_use_existing_native_net_validation_and_preserve_exact_text(PosCommissionBearer bearer)
    {
        var (source, fact) = Original();
        source.PosCommissionAmountText = fact.PosCommissionAmountText = "00.50";
        source.PosBankNetAmountText = fact.PosBankNetAmountText = "1250.00";
        source.PosCommissionBearer = fact.PosCommissionBearer = (int)bearer;
        var view = ReversalProposalService.OriginalReceipt(Company, source.Id, source, fact);
        Assert.NotNull(view); Assert.Equal("00.50", view.PosCommissionAmountText); Assert.Equal("1250.00", view.PosBankNetAmountText);
        Assert.Equal(bearer, view.PosCommissionBearer); Assert.Equal("001250.50", view.AmountText);
        source.PosBankNetAmountText = fact.PosBankNetAmountText = "1249.00";
        Assert.Null(ReversalProposalService.OriginalReceipt(Company, source.Id, source, fact));
    }

    private static (ReceiptDraftRow Source, MoneyEventRow Fact) Original()
    {
        var source = new ReceiptDraftRow { Id = Guid.NewGuid(), CompanyId = Company, Version = 2, Stage = ReceiptStages.Recorded,
            PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), TreasuryAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(),
            AmountText = "001250.50", ProfileId = "SENTETIK-profile", ProfileVersion = "1", OccurredOn = new(2026, 11, 26), ValueOn = new(2026, 11, 27),
            CreatedBy = "recorder", UpdatedBy = "recorder", RecordedBy = "recorder", CreatedAtUtc = At, UpdatedAtUtc = At, RecordedAtUtc = At };
        var fact = new MoneyEventRow { Id = Guid.NewGuid(), CompanyId = Company, SourceId = source.Id, Kind = MoneyEventKinds.ManualBankReceiptRecorded,
            PartyId = source.PartyId, CompanyAccountId = source.CompanyAccountId, TreasuryAccountId = source.TreasuryAccountId, CurrencyId = source.CurrencyId,
            AmountText = source.AmountText, ProfileId = source.ProfileId, ProfileVersion = source.ProfileVersion, OccurredOn = source.OccurredOn, ValueOn = source.ValueOn,
            RecordedBy = source.RecordedBy!, RecordedAtUtc = At, CorrelationId = "record-correlation" };
        return (source, fact);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string name = "receipt-reversal-read-" + Guid.NewGuid();
        public MutableCompany Company { get; } = new(); public User User { get; } = new(); public Permissions Permissions { get; } = new();
        public RecordingCollector Collector { get; } = new(); public ReceiptDraftRow Original { get; private set; } = null!;
        public ReversalProposalRow Proposal { get; private set; } = null!; public FinanceDbContext Database { get; private set; } = null!;
        public ReversalProposalService Core { get; private set; } = null!;
        public FinanceContext Context => new(ReversalProposalDecisionReadTests.Company, "checker", "SENTETIK-read", null);
        public static async Task<Fixture> StartAsync()
        {
            var fixture = new Fixture(); fixture.Company.Set(ReversalProposalDecisionReadTests.Company);
            var (source, fact) = ReversalProposalDecisionReadTests.Original(); fixture.Original = source;
            fixture.Proposal = ReversalProposalPolicy.Propose(source, new(source.Id, "2", new(2026, 11, 27), "SENTETIK reason"), Guid.NewGuid(), "maker", At);
            await using (var seed = fixture.NewDatabase()) { seed.BankAccounts.Add(new BankAccountRow { Id = source.TreasuryAccountId,
                CompanyId = ReversalProposalDecisionReadTests.Company, CurrencyId = source.CurrencyId, DisplayName = "SENTETIK named bank",
                Status = 1, Version = 1, ProposedBy = "bank-maker", ProposedAtUtc = At, UpdatedAtUtc = At }); seed.ReceiptDrafts.Add(source); seed.MoneyEvents.Add(fact); seed.ReversalProposals.Add(fixture.Proposal); await seed.SaveChangesAsync(); }
            fixture.Database = fixture.NewDatabase(); fixture.Core = fixture.Service(fixture.Database, fixture.Collector); return fixture;
        }
        public FinanceDbContext NewDatabase() => new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(name).Options, Company);
        public ReversalProposalService Service(FinanceDbContext db, IAuditEventCollector collector) => new(db, Company, User, Permissions,
            new OpenPeriod(), new NoAllocations(), collector, new(new("propose", [], "Web"), new("approve", [], "Web")), TimeProvider.System);
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
    private sealed class RecordingCollector : IAuditEventCollector
    { public List<AuditEventDraft> Pending { get; } = []; public void Enqueue(AuditEventDraft draft) => Pending.Add(draft); }
    private sealed class MutableCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class User : ICurrentUser
    { public string Actor = "checker"; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id); }
    private sealed class Permissions : IFinancePermissionPort
    { public bool Allowed = true; public bool ProposeOnly; public List<FinanceAction> Actions { get; } = []; public List<FinanceContext> Contexts { get; } = [];
      public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction action, Guid? id, CancellationToken t)
      { Contexts.Add(c); Actions.Add(action); return Task.FromResult(Allowed && (ProposeOnly ? action == FinanceAction.ProposeReversal : action == FinanceAction.ApproveReversal) ? PermissionDecision.Allowed : PermissionDecision.Denied); } }
    private sealed class OpenPeriod : IFinanceRecordPeriodRule
    { public Task<RecordPeriodAnswer> CheckAsync(FinanceContext c, DateOnly d, DateOnly? v, DateTimeOffset n, CancellationToken t) => Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null)); }
    private sealed class NoAllocations : IFinanceReversalAllocationGate
    { public Task<ReversalAllocationAnswer> CheckAsync(FinanceContext c, Guid id, CancellationToken t) => Task.FromResult(new ReversalAllocationAnswer(ReversalAllocationOutcome.NoAllocation)); }
}
