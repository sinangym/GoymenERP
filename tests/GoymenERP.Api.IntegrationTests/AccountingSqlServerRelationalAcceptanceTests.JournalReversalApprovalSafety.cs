using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSqlServerRelationalAcceptanceTests
{
    // Actual migrated SQL and SAME scoped Audit collector/writer; authority/delegation and injected failures are controlled.
    [SqlServerFact]
    public async Task Journal_reversal_owned_audit_save_precommit_and_release_fences_preserve_one_original_native_receipt()
    {
        foreach (var fault in new[] { "after-audit", "after-save", "precommit", "company", "actor", "member", "delegation", "cancel", "postcommit" })
        {
            await using var run = await Run.StartAsync(); var id = await ReversalSafetyPrepare(run);
            var save = new ReversalApprovalSave(id); var committed = new NormalApprovalCommitted();
            await using var scope = new ReversalApprovalScope(run, save, committed);
            var deny = true; var observed = false; var pendingSeen = false; var savedFences = 0;
            using var cancellation = new CancellationTokenSource();
            scope.Authority.Read = () =>
            {
                var staged = scope.Db.ChangeTracker.Entries<AccountingJournalReversalProposalRow>().Any(e => e.Entity.Id == id && e.Entity.Version == 2);
                if (staged && scope.Writer.HasPendingDrafts) pendingSeen = true;
                if (save.Saved) savedFences++;
                var reached = fault == "after-audit" ? pendingSeen && !scope.Writer.HasPendingDrafts && !save.Saved
                    : fault == "precommit" ? savedFences == 3 : fault == "postcommit" ? committed.Commits > 0 : save.Saved;
                if (!deny || !reached) return true;
                observed = true;
                if (fault == "company") { scope.Company.Set(CompanyB); return true; }
                if (fault == "actor") { scope.User.Actor = run.Preparer; return true; }
                if (fault == "member") { scope.User.Member = false; return true; }
                if (fault == "cancel") { cancellation.Cancel(); return true; }
                return false;
            };
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Approve(id, "reversal-owned", cancellation.Token));
            else { var refused = await scope.Approve(id, "reversal-owned"); Assert.Equal(AccountingApiStatus.Denied, refused.Status); Assert.Null(refused.Code); }
            Assert.True(observed);
            if (fault == "precommit") Assert.Equal(3, savedFences); // Saved fence1, native-result validation2, final precommit3.
            scope.AssertClean(); await ReversalSafetyDurable(run, id, fault == "postcommit");
            deny = false; scope.Company.Set(CompanyA); scope.User.Actor = run.Actor; scope.User.Member = true;
            var success = await scope.Approve(id, "reversal-owned"); var replay = await scope.Approve(id, "reversal-owned");
            Assert.Equal(AccountingApiStatus.Ok, success.Status); Assert.True(replay.IsReplay);
            Assert.Equal(JsonSerializer.Serialize(success.Resource), JsonSerializer.Serialize(replay.Resource));
            Assert.Equal(run.Correlation, replay.OriginalCorrelationId); scope.AssertClean(); await ReversalSafetyDurable(run, id, true);
        }
    }

    [SqlServerFact]
    public async Task Journal_reversal_entry_and_cleanup_faults_preserve_caller_work_and_never_start_an_unsafe_winner_read()
    {
        foreach (var fault in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-writer", "rollback", "dispose" })
        {
            await using var run = await Run.StartAsync(); var id = await ReversalSafetyPrepare(run);
            var save = new ReversalApprovalSave(id); var cleanup = fault is "rollback" or "dispose" ? new NormalApprovalCleanup(fault == "dispose") : null;
            await using var scope = new ReversalApprovalScope(run, save, cleanup, fault == "missing-writer");
            await using var borrowed = fault == "borrowed" ? await scope.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var caller = NormalSafetyCaller(); if (fault == "dirty") scope.Db.Commands.Add(caller);
            if (fault == "pending") scope.Collector.Enqueue(NormalSafetyAudit(run));
            var transactions = new HashSet<Guid>();
            scope.Authority.Read = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (fault == "await-dirty" && !scope.Db.ChangeTracker.HasChanges()) scope.Db.Commands.Add(caller);
                if (fault == "await-pending" && !scope.Writer.HasPendingDrafts) scope.Collector.Enqueue(NormalSafetyAudit(run));
                if (cleanup is not null && save.Saved) { cleanup.Armed = true; return false; }
                return true;
            };
            if (cleanup is not null)
            {
                await Assert.ThrowsAsync<AggregateException>(() => scope.Approve(id, "reversal-entry"));
                Assert.True(cleanup.Failed); Assert.Single(transactions); Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(scope.Writer.HasPendingDrafts);
            }
            else
            {
                Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Approve(id, "reversal-entry")).Status);
                Assert.Equal(fault is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges());
                Assert.Equal(fault is "pending" or "await-pending", scope.Writer.HasPendingDrafts);
                if (scope.Db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(scope.Db.ChangeTracker.Entries()).Entity);
                if (borrowed is not null) { Assert.Same(borrowed, scope.Db.Database.CurrentTransaction); await borrowed.RollbackAsync(); }
                else Assert.Null(scope.Db.Database.CurrentTransaction);
            }
            scope.Db.ChangeTracker.Clear(); scope.Writer.DiscardPending(); // Test teardown, never owner entry cleanup.
        }
    }

    [SqlServerFact]
    public async Task Journal_reversal_failed_save_and_controlled_1205_only_read_original_key_after_owned_abort_without_mutation_retry()
    {
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "completed-provider", "audit" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await ReversalSafetyPrepare(run);
            var save = new ReversalApprovalSave(id) { Fault = fault == "audit" ? null : fault };
            await using var scope = new ReversalApprovalScope(run, save, auditDeadlock: fault == "audit");
            var winnerSaved = false; var transactions = new HashSet<Guid>();
            scope.Authority.Read = () => { if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId); return true; };
            scope.Authority.BeforeRead = async () =>
            {
                var injected = save.Injected || scope.Writer is NormalAuditDeadlockWriter { Injected: true };
                if (!injected || scope.Db.Database.CurrentTransaction is not null || winnerSaved || !winner) return;
                scope.AssertClean(); winnerSaved = true;
                await using var other = new ReversalApprovalScope(run);
                Assert.Equal(AccountingApiStatus.Ok, (await other.Approve(id, "reversal-race")).Status);
            };
            var result = await scope.Approve(id, "reversal-race");
            Assert.Equal(fault == "audit" ? 0 : 1, save.Attempts); Assert.Equal(2, transactions.Count);
            Assert.Equal(winner, result.IsReplay); Assert.Equal(winner ? AccountingApiStatus.Ok : AccountingApiStatus.Conflict, result.Status);
            if (!winner) Assert.Equal(AccountingStoreCodes.ConcurrentChange, result.Code);
            scope.AssertClean(); await ReversalSafetyDurable(run, id, winner);
        }
    }

    private static async Task<Guid> ReversalSafetyPrepare(Run run)
    {
        var period = await run.OpenSeptemberAsync(CompanyA); var id = Guid.NewGuid();
        await using var scope = run.Store(CompanyA);
        var source = (await scope.Postings.ReceiveAsync(Request(CompanyA, "reversal-source-" + id.ToString("N"), Guid.NewGuid(), Accounts(CompanyA)), period, Metadata(run))).Journal!;
        var proposal = AccountingJournalReversalPolicyV1.Propose(CompanyA, run.Preparer,
            new(id, source.Id, period, InSeptember.TransactionDate, InSeptember.AccountingDate, InSeptember.ValueDate, "SENTETIK reversal"), RecordedAt);
        await ((IAccountingJournalReversalProposalStoreV1)scope.Postings).CreateAsync(proposal, _ => Task.CompletedTask, Metadata(run));
        return id;
    }
    private static async Task ReversalSafetyDurable(Run run, Guid id, bool approved)
    {
        Assert.Equal(approved ? 2 : 1, await run.ScalarAsync<int>("SELECT Version FROM accounting.JournalReversalProposals WHERE Id=@id", ("@id", id)));
        Assert.Equal(approved ? 2 : 1, await run.ScalarAsync<int>("SELECT Status FROM accounting.JournalReversalProposals WHERE Id=@id", ("@id", id)));
        Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals j JOIN accounting.JournalReversalProposals p ON p.ReversalJournalId=j.Id AND p.CompanyId=j.CompanyId WHERE p.Id=@id", ("@id", id)));
        Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName=@n", ("@n", AccountingJournalReversalCommandNamesV1.Approve)));
        Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'accounting.journal-reversal.approved.v1'", ("@id", id.ToString("D"))));
    }
    private sealed class ReversalApprovalScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public NormalApprovalCompany Company { get; } = new(); public NormalApprovalUser User { get; }
        public ReversalApprovalAuthority Authority { get; } = new();
        public AccountingDbContext Db { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public AccountingJournalReversalServiceV1 Service { get; }
        public ReversalApprovalScope(Run run, ReversalApprovalSave? save = null, IInterceptor? extra = null, bool missingWriter = false, bool auditDeadlock = false)
        {
            Company.Set(CompanyA); User = new(run.Actor); auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
            var actual = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>(); Writer = auditDeadlock ? new NormalAuditDeadlockWriter(actual) : actual;
            var options = new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra); if (extra is NormalApprovalCleanup cleanup) cleanup.Configure(options);
            Db = new(options.Options, Company); var core = new AccountingStore(Db, Company, Collector, TimeProvider.System,
                new AccountingAmountScaleVerifier(new SyntheticMoneyProfiles(CompanyA)), normalApprovalAuditWriter: missingWriter ? null : Writer);
            var correlation = new CorrelationContext(); correlation.Set(run.Correlation, null, "Web", null, null);
            Service = new(core, core, Authority, Company, User, correlation, TimeProvider.System);
        }
        public Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> Approve(Guid id, string key, CancellationToken token = default) => Service.ApproveAsync(id, 1, key, token);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class ReversalApprovalAuthority : IErpAuthorityRevalidationService
    {
        public Func<bool>? Read; public Func<Task>? BeforeRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Assert.Equal(AccountingJournalReversalPermissionNamesV1.Approve, permission); if (BeforeRead is not null) await BeforeRead(); return new(Read?.Invoke() ?? true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); }
    }
    private sealed class ReversalApprovalSave(Guid id) : SaveChangesInterceptor
    {
        public string? Fault; public bool Saved, Injected; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is AccountingDbContext db && db.ChangeTracker.Entries<AccountingJournalReversalProposalRow>().Any(e => e.Entity.Id == id && e.State == EntityState.Modified && e.Entity.Version == 2))
            {
                Attempts++;
                if (Fault is not null)
                {
                    Injected = true; if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                    if (Fault == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK reversal save race");
                    var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); if (Fault == "wrapped") throw new DbUpdateException("SENTETIK 1205", error); throw error;
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
}
