using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Inventory_cost_owned_approve_preserves_posted_zero_stale_original_receipts_and_later_replay_once()
    {
        foreach (var status in new[] { "Posted", "RoundedNoCost", "Stale" })
        foreach (var purchase in new[] { false, true })
        {
            await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run, purchase, status == "RoundedNoCost");
            var before = await InventoryApprovalCounts(run); if (status == "Stale") setup.Profiles.Version++;
            await using var owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles);
            var first = await owner.Approve(setup.Id, setup.Kind, "original"); var replay = await owner.Approve(setup.Id, setup.Kind, "original");
            Assert.Equal(status == "Stale" ? AccountingApiStatus.Conflict : AccountingApiStatus.Ok, first.Status);
            Assert.Equal(status, first.Resource?.Status); Assert.Equal(3L, first.Resource?.Version);
            Assert.True(replay.IsReplay); Assert.Equal(owner.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(first.Resource?.ProposalId, replay.Resource?.ProposalId); Assert.Equal(first.Resource?.JournalId, replay.Resource?.JournalId);
            var posted = status == "Posted" ? 1 : 0;
            Assert.Equal(before with { Proposals = before.Proposals + 1, Commands = before.Commands + 1, Journals = before.Journals + posted,
                Deliveries = before.Deliveries + posted, Audits = before.Audits + 1 + posted }, await InventoryApprovalCounts(run));
            Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await owner.Service.ApproveAsync(setup.Id, setup.Kind, 3, "original")).Code);
            owner.User.Actor = Maker; Assert.Equal(AccountingApiCodes.IdempotencyConflict, (await owner.Approve(setup.Id, setup.Kind, "original")).Code);
            Assert.Equal(before.Commands + 1, await run.Count("accounting.Commands")); owner.AssertClean();
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_approval_entry_preserves_borrowed_ambient_dirty_and_pending_caller_state()
    {
        foreach (var fault in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-writer", "other-context", "other-collector", "other-writer" })
        {
            await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run); var before = await InventoryApprovalCounts(run);
            await using (var owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles, entryFault: fault))
            {
                var caller = InventoryApprovalCaller();
                if (fault == "dirty") owner.Db.Commands.Add(caller); if (fault == "pending") owner.Collector.Enqueue(InventoryApprovalAudit());
                await using var borrowed = fault == "borrowed" ? await owner.Db.Database.BeginTransactionAsync() : null;
                using (var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null)
                {
                    owner.Authority.BeforeRead = () => { if (fault == "await-dirty") owner.Db.Commands.Add(caller); if (fault == "await-pending") owner.Collector.Enqueue(InventoryApprovalAudit()); return Task.CompletedTask; };
                    Assert.Equal(AccountingApiStatus.Unavailable, (await owner.Approve(setup.Id, setup.Kind, "entry")).Status);
                    Assert.Equal(0, owner.Writer.Writes); Assert.Equal(0, owner.Writer.Discards);
                    Assert.Equal(fault is "dirty" or "await-dirty", owner.Db.ChangeTracker.HasChanges());
                    Assert.Equal(fault is "pending" or "await-pending", owner.Writer.HasPendingDrafts);
                    if (owner.Db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(owner.Db.ChangeTracker.Entries()).Entity);
                    if (borrowed is not null) Assert.Same(borrowed, owner.Db.Database.CurrentTransaction);
                }
                if (borrowed is not null) await borrowed.RollbackAsync();
            }
            Assert.Equal(before, await InventoryApprovalCounts(run));
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_audit_save_precommit_and_postcommit_fences_roll_back_or_suppress_exactly_once()
    {
        foreach (var phase in new[] { "before-audit", "after-audit", "after-save", "precommit", "actor", "member", "company", "cancel", "postcommit" })
        {
            await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run); var before = await InventoryApprovalCounts(run);
            var save = new InventoryApprovalSave(); var tx = new InventoryApprovalTransactions();
            await using var owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles, save, tx);
            using var cancellation = new CancellationTokenSource(); var savedFences = 0; var sawPending = false; var triggered = false;
            owner.Authority.Read = () =>
            {
                var staged = owner.Db.ChangeTracker.Entries<AccountingInventoryCostProposalRow>().Any(x => x.Entity.Id == setup.Id && x.Entity.Status == 3);
                if (staged && owner.Writer.HasPendingDrafts) sawPending = true;
                if (save.SavedProposal) savedFences++;
                // Final proposal save: post-save1, Atomic release2, service3, runner4, final precommit5.
                var fire = phase switch { "before-audit" => staged && owner.Writer.HasPendingDrafts,
                    "after-audit" => staged && sawPending && !owner.Writer.HasPendingDrafts && !save.SavedProposal,
                    "precommit" => savedFences == 5, "postcommit" => tx.Commits == 1, _ => save.SavedProposal };
                if (!fire) return true; triggered = true;
                if (phase == "actor") owner.User.Actor = Maker;
                if (phase == "member") owner.User.Member = false;
                if (phase == "company") owner.Company.Set(TestCompanyIds.CompanyB);
                if (phase == "cancel") cancellation.Cancel();
                return phase is "actor" or "member" or "company" or "cancel";
            };
            if (phase == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.Approve(setup.Id, setup.Kind, "late", cancellation.Token));
            else { var refused = await owner.Approve(setup.Id, setup.Kind, "late"); Assert.Equal(AccountingApiStatus.Denied, refused.Status); Assert.Null(refused.Resource); }
            Assert.True(triggered); Assert.Equal(before with { Proposals = before.Proposals + (phase == "postcommit" ? 1 : 0), Commands = before.Commands + (phase == "postcommit" ? 1 : 0),
                Journals = before.Journals + (phase == "postcommit" ? 1 : 0), Deliveries = before.Deliveries + (phase == "postcommit" ? 1 : 0), Audits = before.Audits + (phase == "postcommit" ? 2 : 0) }, await InventoryApprovalCounts(run));
            owner.AssertClean(); owner.Authority.Read = null; owner.User.Actor = Checker; owner.User.Member = true; owner.Company.Set(Company);
            var accepted = await owner.Approve(setup.Id, setup.Kind, "late"); var replay = await owner.Approve(setup.Id, setup.Kind, "late");
            Assert.Equal(AccountingApiStatus.Ok, accepted.Status); Assert.Equal(phase == "postcommit", accepted.IsReplay); Assert.True(replay.IsReplay);
            Assert.Equal(before.Commands + 1, await run.Count("accounting.Commands")); Assert.Equal(before.Journals + 1, await run.Count("accounting.Journals"));
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_injected_save_or_1205_failure_aborts_before_one_readonly_original_key_winner_without_mutation_retry()
    {
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "completed-provider", "audit" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run); var before = await InventoryApprovalCounts(run);
            var save = new InventoryApprovalSave { Fault = fault == "audit" ? null : fault }; var tx = new InventoryApprovalTransactions();
            await using var owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles, save, tx); owner.Writer.InjectDeadlock = fault == "audit";
            var won = false; string? original = null;
            owner.Authority.BeforeRead = async () =>
            {
                if (!winner || won || !(save.Injected || owner.Writer.Injected) || owner.Db.Database.CurrentTransaction is not null) return;
                won = true; await using var other = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles);
                var answer = await other.Approve(setup.Id, setup.Kind, "race"); Assert.Equal(AccountingApiStatus.Ok, answer.Status); original = other.CorrelationId;
            };
            var result = await owner.Approve(setup.Id, setup.Kind, "race");
            Assert.Equal(winner ? AccountingApiStatus.Ok : AccountingApiStatus.Conflict, result.Status);
            Assert.Equal(winner, result.IsReplay); if (winner) Assert.Equal(original, result.OriginalCorrelationId); else Assert.Null(result.Resource);
            Assert.Equal(2, tx.Starts); Assert.Equal(fault == "audit" ? 0 : 1, save.Attempts); owner.AssertClean();
            Assert.Equal(before with { Proposals = before.Proposals + (winner ? 1 : 0), Commands = before.Commands + (winner ? 1 : 0), Journals = before.Journals + (winner ? 1 : 0),
                Deliveries = before.Deliveries + (winner ? 1 : 0), Audits = before.Audits + (winner ? 2 : 0) }, await InventoryApprovalCounts(run));
            // Provider error initiation is controlled injection, not a naturally reproduced concurrent deadlock.
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_final_proposal_save_race_aborts_before_one_original_key_read_without_repeating_mutation()
    {
        foreach (var status in new[] { "Posted", "RoundedNoCost", "Stale" })
        foreach (var fault in new[] { "final-concurrency", "final-2601", "final-2627" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run, zero: status == "RoundedNoCost");
            if (status == "Stale") setup.Profiles.Version++;
            var before = await InventoryApprovalCounts(run); var save = new InventoryApprovalSave { Fault = fault }; var tx = new InventoryApprovalTransactions();
            await using var owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles, save, tx);
            var won = false; string? original = null;
            owner.Authority.BeforeRead = async () =>
            {
                if (!winner || won || !save.Injected || owner.Db.Database.CurrentTransaction is not null) return;
                won = true; await using var other = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles);
                var answer = await other.Approve(setup.Id, setup.Kind, "final-race");
                Assert.Equal(status, answer.Resource?.Status); original = other.CorrelationId;
            };
            var result = await owner.Approve(setup.Id, setup.Kind, "final-race");
            Assert.True(save.Injected); Assert.Equal(status == "Posted" ? 2 : 1, save.Attempts);
            Assert.Equal(2, tx.Starts); Assert.Equal(1, owner.Writer.Discards); owner.AssertClean();
            Assert.Equal(winner, result.IsReplay); Assert.Equal(winner && status != "Stale" ? AccountingApiStatus.Ok : AccountingApiStatus.Conflict, result.Status);
            if (winner) { Assert.Equal(status, result.Resource?.Status); Assert.Equal(3L, result.Resource?.Version); Assert.Equal(original, result.OriginalCorrelationId); }
            else { Assert.Equal(AccountingStoreCodes.ConcurrentChange, result.Code); Assert.Null(result.Resource); }
            var posted = winner && status == "Posted" ? 1 : 0;
            Assert.Equal(before with { Proposals = before.Proposals + (winner ? 1 : 0), Commands = before.Commands + (winner ? 1 : 0),
                Journals = before.Journals + posted, Deliveries = before.Deliveries + posted, Audits = before.Audits + (winner ? 1 : 0) + posted }, await InventoryApprovalCounts(run));
            // The failure is injected only at the final proposal/Commands save on real SQL, after the journal save when Posted.
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_terminal_refusal_save_race_aborts_before_one_original_key_read_without_repeating_command()
    {
        foreach (var fault in new[] { "terminal-concurrency", "terminal-2601", "terminal-2627" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run); var before = await InventoryApprovalCounts(run);
            var save = new InventoryApprovalSave { Fault = fault }; var tx = new InventoryApprovalTransactions();
            await using var owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles, save, tx);
            var won = false; string? original = null;
            owner.Authority.BeforeRead = async () =>
            {
                if (!winner || won || !save.Injected || owner.Db.Database.CurrentTransaction is not null) return;
                won = true; await using var other = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles);
                var answer = await other.Service.ApproveAsync(setup.Id, setup.Kind, 99, "terminal-race");
                Assert.Equal(AccountingApiCodes.VersionConflict, answer.Code); original = other.CorrelationId;
            };
            var result = await owner.Service.ApproveAsync(setup.Id, setup.Kind, 99, "terminal-race");
            Assert.True(save.Injected); Assert.Equal(1, save.Attempts); Assert.Equal(2, tx.Starts); Assert.Equal(1, owner.Writer.Discards); owner.AssertClean();
            Assert.Equal(AccountingApiStatus.Conflict, result.Status); Assert.Equal(winner, result.IsReplay); Assert.Null(result.Resource);
            Assert.Equal(winner ? AccountingApiCodes.VersionConflict : AccountingStoreCodes.ConcurrentChange, result.Code);
            if (winner) { Assert.Equal(99L, result.ExpectedVersion); Assert.Equal(2L, result.CurrentVersion); Assert.Equal(original, result.OriginalCorrelationId); }
            Assert.Equal(before with { Commands = before.Commands + (winner ? 1 : 0) }, await InventoryApprovalCounts(run));
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_cleanup_fault_preserves_aggregate_failure_and_still_clears_tracker_and_owned_audit()
    {
        foreach (var dispose in new[] { false, true })
        {
            await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run);
            var save = new InventoryApprovalSave { Fault = "concurrency" }; var cleanup = new InventoryApprovalCleanup(dispose);
            await using var owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles, save, cleanup);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => owner.Approve(setup.Id, setup.Kind, "cleanup"));
            Assert.Contains("cleanup failed", failure.Message, StringComparison.Ordinal); Assert.True(cleanup.Failed);
            Assert.False(owner.Db.ChangeTracker.HasChanges()); Assert.False(owner.Writer.HasPendingDrafts); Assert.Equal(1, owner.Writer.Discards);
            Assert.Equal(1, save.Attempts); // No winner read after failed cleanup. Durability is deliberately not claimed here.
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_late_saved_journal_business_refusal_rolls_back_and_leaves_same_key_available()
    {
        await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run); var before = await InventoryApprovalCounts(run);
        var save = new InventoryApprovalSave { Fault = "after-save-domain" };
        await using var owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles, save);
        var refused = await owner.Approve(setup.Id, setup.Kind, "business");
        Assert.Equal(AccountingApiStatus.Unprocessable, refused.Status); Assert.Equal("inventory_cost_posting_refused", refused.Code); Assert.Null(refused.Resource);
        Assert.True(save.Injected); Assert.Equal(before, await InventoryApprovalCounts(run)); Assert.Equal(1, owner.Writer.Discards); owner.AssertClean();
        save.Fault = null;
        Assert.Equal(AccountingApiStatus.Ok, (await owner.Approve(setup.Id, setup.Kind, "business")).Status);
        Assert.True((await owner.Approve(setup.Id, setup.Kind, "business")).IsReplay);
        Assert.Equal(before.Commands + 1, await run.Count("accounting.Commands")); Assert.Equal(before.Journals + 1, await run.Count("accounting.Journals"));
    }
    [SqlServerFact]
    public async Task Inventory_cost_authority_loss_after_actual_source_query_stops_before_plan_or_currency_reads()
    {
        foreach (var target in new[] { "InventoryCostSources", "Periods" })
        {
            await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run); var before = await InventoryApprovalCounts(run);
            InventoryApprovalSqlScope? owner = null; var hit = false;
            var probe = new InventoryApprovalQuery(sql => { if (!sql.Contains("[" + target + "]", StringComparison.Ordinal)) return; hit = true; owner!.Authority.Granted = false; });
            await using var active = owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles, extra: probe);
            var answer = await owner.Approve(setup.Id, setup.Kind, "read-fence");
            Assert.True(hit); Assert.Equal(AccountingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource);
            Assert.Equal(0, owner.Plan.Reads); Assert.Equal(0, owner.Currencies.Reads); owner.AssertClean(); Assert.Equal(before, await InventoryApprovalCounts(run));
        }
    }
    [SqlServerFact]
    public async Task Inventory_cost_saved_version_refusal_replays_original_answer_after_later_posting_without_mutating_again()
    {
        await using var run = await Run.Start(); var setup = await InventoryApprovalPrepare(run); var before = await InventoryApprovalCounts(run);
        await using var owner = new InventoryApprovalSqlScope(run, setup.Reader, setup.Profiles);
        owner.User.Actor = Maker;
        var own = await owner.Approve(setup.Id, setup.Kind, "maker");
        Assert.Equal(AccountingApiStatus.Denied, own.Status); Assert.Equal("inventory_cost_approval_refused", own.Code);
        Assert.Equal(before, await InventoryApprovalCounts(run)); owner.AssertClean(); owner.User.Actor = Checker;
        var refusal = await owner.Service.ApproveAsync(setup.Id, setup.Kind, 99, "old-refusal");
        Assert.Equal(AccountingApiStatus.Conflict, refusal.Status); Assert.Equal(AccountingApiCodes.VersionConflict, refusal.Code);
        Assert.Equal(99L, refusal.ExpectedVersion); Assert.Equal(2L, refusal.CurrentVersion); Assert.Null(refusal.Resource);
        Assert.Equal(before.Commands + 1, await run.Count("accounting.Commands"));
        Assert.Equal(AccountingApiStatus.Ok, (await owner.Approve(setup.Id, setup.Kind, "later-posting")).Status);
        var replay = await owner.Service.ApproveAsync(setup.Id, setup.Kind, 99, "old-refusal");
        Assert.True(replay.IsReplay); Assert.Equal(owner.CorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(AccountingApiStatus.Conflict, replay.Status); Assert.Equal(99L, replay.ExpectedVersion); Assert.Equal(2L, replay.CurrentVersion);
        Assert.Equal(before.Commands + 2, await run.Count("accounting.Commands")); Assert.Equal(before.Journals + 1, await run.Count("accounting.Journals"));
        Assert.Equal(before.Proposals + 1, await run.Count("accounting.InventoryCostProposals")); owner.AssertClean();
        // Unchanged entities retained from a prior successful command are not this new attempt's writes.
        var fresh = await owner.Service.ApproveAsync(setup.Id, setup.Kind, 99, "new-refusal-after-posting");
        Assert.Equal(AccountingApiStatus.Conflict, fresh.Status); Assert.False(fresh.IsReplay); Assert.Equal(3L, fresh.CurrentVersion);
        Assert.Equal(before.Commands + 3, await run.Count("accounting.Commands")); Assert.Equal(before.Journals + 1, await run.Count("accounting.Journals")); owner.AssertClean();
    }
    private sealed record InventoryApprovalSetup(Guid Id, AccountingInventoryCostKindV1 Kind, RoundedCostReader Reader, MutableCostProfiles Profiles);
    private static async Task<InventoryApprovalSetup> InventoryApprovalPrepare(Run run, bool purchase = false, bool zero = false)
    {
        var period = await run.Period(); var reader = new RoundedCostReader(run.Source.Facts, zero ? new("1", "250") : new("1", "3"), purchase); var profiles = new MutableCostProfiles();
        await using var scope = run.Open(); var store = Costs(scope, reader, profiles); await store.AcceptAsync(reader.Envelope);
        var kind = purchase ? AccountingInventoryCostKindV1.PurchaseInvoiceDifference : AccountingInventoryCostKindV1.SalesInvoicedShipment;
        var source = Assert.Single(await store.ListAsync(kind)); await store.PrepareAsync(source.ProposalId, kind, CostPreparation(period), Maker, Fence, Metadata(Maker));
        return new(source.ProposalId, kind, reader, profiles);
    }
    private sealed record InventoryApprovalCount(int Proposals, int Commands, int Journals, int Deliveries, int Audits);
    private static async Task<InventoryApprovalCount> InventoryApprovalCounts(Run run) => new(await run.Count("accounting.InventoryCostProposals"), await run.Count("accounting.Commands"),
        await run.Count("accounting.Journals"), await run.Count("accounting.Deliveries"), await run.Count("audit.AuditEvents"));
    private sealed class InventoryApprovalSqlScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope; private readonly AccountingDbContext other;
        public InventoryApprovalCompany Company { get; } = new(); public InventoryApprovalUser User { get; } = new(); public InventoryApprovalAuthority Authority { get; } = new();
        public AccountingDbContext Db { get; } public AuditEventCollector Collector { get; } public InventoryApprovalWriter Writer { get; }
        public InventoryApprovalPlan Plan { get; } = new(); public InventoryApprovalCurrencies Currencies { get; } = new();
        public AccountingInventoryCostServiceV1 Service { get; } public string CorrelationId { get; } = "SENTETIK-inventory-approval-" + Guid.NewGuid().ToString("N");
        public InventoryApprovalSqlScope(Run run, IAccountingInventoryCostSourceReaderV1 reader, ISettingsPrerequisiteProvider profiles,
            InventoryApprovalSave? save = null, IInterceptor? extra = null, string? entryFault = null)
        {
            auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(AccountingInvoiceLineCogsSqlServerRelationalAcceptanceTests.Company);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = new(auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
            var options = new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString).AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra); if (extra is InventoryApprovalCleanup cleanup) cleanup.Configure(options);
            Db = new(options.Options, Company); other = new(options.Options, Company);
            var core = new AccountingStore(entryFault == "other-context" ? other : Db, Company,
                entryFault == "other-collector" ? new AuditEventCollector() : Collector, TimeProvider.System, new(profiles),
                normalApprovalAuditWriter: entryFault == "missing-writer" ? null : entryFault == "other-writer" ? new InventoryApprovalWriter(Writer) : Writer);
            var store = new AccountingInventoryCostStoreV1(Db, core, reader, new CogsMapping(), Plan, Currencies, new(profiles), Company, Collector, Writer, TimeProvider.System);
            var correlation = new CorrelationContext(); correlation.Set(CorrelationId, null, SourceChannels.Web, null, null);
            Service = new(store, core, Authority, Company, User, correlation);
        }
        public Task<AccountingApiResult<AccountingInventoryCostProposalV1>> Approve(Guid id, AccountingInventoryCostKindV1 kind, string key, CancellationToken token = default) => Service.ApproveAsync(id, kind, 2, key, token);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); await other.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class InventoryApprovalCompany : ICompanyContext { public Guid CompanyId { get; private set; } = Company; public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid id) => CompanyId = id; }
    private sealed class InventoryApprovalUser : ICurrentUser { public Guid Actor = Checker; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class InventoryApprovalAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted = true; public Func<bool>? Read; public Func<Task>? BeforeRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { if (BeforeRead is not null) await BeforeRead(); return new(Granted && (Read?.Invoke() ?? true), true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); }
    }
    private sealed class InventoryApprovalPlan : IActiveAccountPlanReaderV1
    { public int Reads; public async Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActiveAsync(CancellationToken token = default) { Reads++; return await new Plan().ReadActiveAsync(token); } }
    private sealed class InventoryApprovalCurrencies : ISettingsMoneyCandidateReader
    { public int Reads; public async Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken token = default) { Reads++; return await new CostCurrencies().GetMoneyCurrencyCandidatesAsync(token); } }
    private sealed class InventoryApprovalWriter(IAuditTransactionWriterV1 actual) : IAuditTransactionWriterV1
    { public int Writes, Discards; public bool InjectDeadlock, Injected; public bool HasPendingDrafts => actual.HasPendingDrafts;
      public Task WritePendingAsync(System.Data.IDbTransaction tx, CancellationToken token = default) { Writes++; if (InjectDeadlock && !Injected) { Injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); } return actual.WritePendingAsync(tx, token); }
      public void DiscardPending() { Discards++; actual.DiscardPending(); } }
    private sealed class InventoryApprovalSave : SaveChangesInterceptor
    {
        public string? Fault; public bool Injected, SavedProposal; public int Attempts; private bool proposalSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is AccountingDbContext db && (db.ChangeTracker.Entries<JournalRow>().Any(e => e.State == EntityState.Added)
                || db.ChangeTracker.Entries<AccountingInventoryCostProposalRow>().Any(e => e.State == EntityState.Added && e.Entity.Status is 3 or 6 or 7)
                || Fault?.StartsWith("terminal-", StringComparison.Ordinal) == true && db.ChangeTracker.Entries<AccountingCommandRow>().Any(e => e.State == EntityState.Added)))
            {
                Attempts++; proposalSave = db.ChangeTracker.Entries<AccountingInventoryCostProposalRow>().Any(e => e.State == EntityState.Added && e.Entity.Status is 3 or 6 or 7);
                var targetReached = Fault?.StartsWith("final-", StringComparison.Ordinal) != true || proposalSave;
                if (Fault is not null && Fault != "after-save-domain" && !Injected && targetReached)
                {
                    Injected = true; if (Fault is "concurrency" or "final-concurrency" or "terminal-concurrency") throw new DbUpdateConcurrencyException("SENTETIK inventory approval race");
                    if (Fault is "final-2601" or "terminal-2601" or "final-2627" or "terminal-2627")
                        throw new DbUpdateException("SENTETIK final inventory command duplicate", SqlConcurrencyWrapperPolicyTests.SqlFailure(Fault.EndsWith("2601", StringComparison.Ordinal) ? 2601 : 2627));
                    if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                    var failure = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); if (Fault == "wrapped") throw new DbUpdateException("SENTETIK wrapped provider failure", failure); throw failure;
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (proposalSave) SavedProposal = true; if (Fault == "after-save-domain" && !Injected) { Injected = true; throw new AccountingDomainException("SENTETIK-inventory-cost-domain", "SENTETIK business refusal after actual journal save"); } return ValueTask.FromResult(result); }
    }
    private sealed class InventoryApprovalTransactions : DbTransactionInterceptor
    {
        public int Starts, Commits;
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData data, DbTransaction result, CancellationToken token = default) { Starts++; return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default) { Commits++; return Task.CompletedTask; }
    }
    private sealed class InventoryApprovalCleanup(bool dispose) : DbTransactionInterceptor
    {
        public bool Failed;
        public void Configure(DbContextOptionsBuilder<AccountingDbContext> options) => options.LogTo((id, _) => id == RelationalEventId.TransactionDisposed,
            _ => { if (dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK inventory dispose fault"); } });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (!dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK inventory rollback fault"); } return ValueTask.FromResult(result); }
    }
    private sealed class InventoryApprovalQuery(Action<string> after) : DbCommandInterceptor
    { public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default) { after(command.CommandText); return ValueTask.FromResult(result); } }
    private static AccountingCommandRow InventoryApprovalCaller() => new() { CompanyId = Company, CommandName = "SENTETIK-caller", IdempotencyKeyHash = "caller", ActorId = "caller", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
    private static AuditEventDraft InventoryApprovalAudit() => new(Company, "SENTETIK-caller", "Accounting.InventoryCost", "caller", "Approve", 2, 3, "User", Checker.ToString("D"), NormalJournalPermissionNamesV1.DraftApprove,
        [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None, null, SourceChannels.Web, null, null, "SENTETIK-caller", null, "key");
}
