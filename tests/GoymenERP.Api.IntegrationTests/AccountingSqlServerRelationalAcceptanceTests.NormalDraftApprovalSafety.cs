using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSqlServerRelationalAcceptanceTests
{
    // Existing migrated SQL fixture and SAME scoped real Audit writer. Authority/delegation answers are controlled.
    [SqlServerFact]
    public async Task Normal_approval_owned_final_save_audit_and_precommit_refusal_roll_back_posting_receipt_and_period_then_same_key_posts_once()
    {
        foreach (var fault in new[] { "after-audit", "after-save", "precommit", "company", "actor", "member", "delegation", "cancel" })
        {
            await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run);
            var periodVersion = await run.ScalarAsync<long>("SELECT Version FROM accounting.Periods WHERE Id=@id", ("@id", period));
            var save = new NormalApprovalSave(id); await using var scope = new NormalApprovalScope(run, save);
            var deny = true; var savedFences = 0; var observed = false; var pending = false; using var cancellation = new CancellationTokenSource();
            scope.Authority.Read = () =>
            {
                var staged = scope.Db.ChangeTracker.Entries<NormalJournalDraftRow>().Any(x => x.Entity.Id == id && x.Entity.Version == 2);
                if (staged && scope.Writer.HasPendingDrafts) pending = true;
                if (save.FinalSaved) savedFences++;
                var reached = fault == "after-audit" ? staged && pending && !scope.Writer.HasPendingDrafts : fault == "precommit" ? savedFences == 4 : save.FinalSaved;
                if (!deny || !reached) return true;
                observed = true;
                if (fault == "company") { scope.Company.Set(CompanyB); return true; }
                if (fault == "actor") { scope.User.Actor = run.Preparer; return true; }
                if (fault == "member") { scope.User.Member = false; return true; }
                if (fault == "cancel") { cancellation.Cancel(); return true; }
                return false; // Includes current Work delegation refusal at the SAME native authority boundary, not a full WP3 E2E.
            };
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Approve(id, period, "normal-owned-late", cancellation.Token));
            else Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(id, period, "normal-owned-late")).Status);
            Assert.True(observed); if (fault == "precommit") Assert.Equal(4, savedFences); // post-final-Save1, service result release2, owner result3, precommit4.
            scope.AssertClean(); await NormalSafetyDurable(run, id, false);
            Assert.Equal(periodVersion, await run.ScalarAsync<long>("SELECT Version FROM accounting.Periods WHERE Id=@id", ("@id", period)));
            deny = false; scope.Company.Set(CompanyA); scope.User.Actor = run.Actor; scope.User.Member = true; save.FinalSaved = false;
            var success = await scope.Approve(id, period, "normal-owned-late"); var replay = await scope.Approve(id, period, "normal-owned-late");
            Assert.Equal(AccountingApiStatus.Ok, success.Status); Assert.True(replay.IsReplay); Assert.Equal(JsonSerializer.Serialize(success.Resource), JsonSerializer.Serialize(replay.Resource));
            Assert.Equal(run.Correlation, replay.OriginalCorrelationId); scope.AssertClean(); await NormalSafetyDurable(run, id, true);
            Assert.Equal(periodVersion + 1, await run.ScalarAsync<long>("SELECT Version FROM accounting.Periods WHERE Id=@id", ("@id", period)));
        }
    }

    [SqlServerFact]
    public async Task Normal_approval_clean_entry_preserves_borrowed_ambient_dirty_and_pending_caller_state_before_native_reads()
    {
        foreach (var fault in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-writer" })
        {
            await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run); await using var scope = new NormalApprovalScope(run, missingWriter: fault == "missing-writer");
            await using var borrowed = fault == "borrowed" ? await scope.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var caller = NormalSafetyCaller(); if (fault == "dirty") scope.Db.Commands.Add(caller);
            if (fault == "pending") scope.Collector.Enqueue(NormalSafetyAudit(run));
            scope.Authority.Read = () => { if (fault == "await-dirty" && !scope.Db.ChangeTracker.HasChanges()) scope.Db.Commands.Add(caller);
                if (fault == "await-pending" && !scope.Writer.HasPendingDrafts) scope.Collector.Enqueue(NormalSafetyAudit(run)); return true; };
            var result = await scope.Approve(id, period, "normal-entry"); Assert.Equal(AccountingApiStatus.Unavailable, result.Status);
            Assert.Equal(fault is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges()); Assert.Equal(fault is "pending" or "await-pending", scope.Writer.HasPendingDrafts);
            if (scope.Db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(scope.Db.ChangeTracker.Entries()).Entity);
            if (borrowed is not null) { Assert.Same(borrowed, scope.Db.Database.CurrentTransaction); await borrowed.RollbackAsync(); }
            else Assert.Null(scope.Db.Database.CurrentTransaction);
            // Test teardown only; the refused owner has not cleared or discarded the caller's work.
            scope.Db.ChangeTracker.Clear(); scope.Writer.DiscardPending();
        }
    }

    [SqlServerFact]
    public async Task Normal_approval_controlled_save_failures_and_1205_abort_before_one_readonly_original_key_pass_without_mutation_retry()
    {
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "completed-provider" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run); var save = new NormalApprovalSave(id) { Fault = fault };
            await using var scope = new NormalApprovalScope(run, save); var transactions = new HashSet<Guid>(); var winnerCommitted = false;
            scope.Authority.Read = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                return true;
            };
            // Invoked at the first authority boundary AFTER rollback+dispose, before starting the read-only winner transaction.
            scope.Authority.BeforeRead = async () =>
            {
                if (!save.Injected || scope.Db.Database.CurrentTransaction is not null || winnerCommitted || !winner) return;
                scope.AssertClean(); winnerCommitted = true;
                await using var other = new NormalApprovalScope(run); Assert.Equal(AccountingApiStatus.Ok, (await other.Approve(id, period, "normal-race")).Status);
            };
            var result = await scope.Approve(id, period, "normal-race"); Assert.True(save.Injected); Assert.Equal(1, save.FinalAttempts);
            Assert.Equal(2, transactions.Count); Assert.Equal(winner, result.IsReplay); Assert.Equal(winner ? AccountingApiStatus.Ok : AccountingApiStatus.Conflict, result.Status);
            if (!winner) Assert.Equal(AccountingStoreCodes.ConcurrentChange, result.Code);
            scope.AssertClean(); await NormalSafetyDurable(run, id, winner);
        }
    }

    [SqlServerFact]
    public async Task Normal_approval_cleanup_faults_clean_owned_tracker_and_audit_without_attempting_a_winner_read()
    {
        foreach (var dispose in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run); var save = new NormalApprovalSave(id); var cleanup = new NormalApprovalCleanup(dispose);
            await using var scope = new NormalApprovalScope(run, save, cleanup); var transactions = new HashSet<Guid>();
            scope.Authority.Read = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (!save.FinalSaved) return true; cleanup.Armed = true; return false;
            };
            await Assert.ThrowsAsync<AggregateException>(() => scope.Approve(id, period, "normal-cleanup-fault"));
            Assert.True(cleanup.Failed); Assert.Single(transactions); Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(scope.Writer.HasPendingDrafts);
        }
    }

    [SqlServerFact]
    public async Task Normal_approval_postcommit_refusal_keeps_original_receipt_and_native_replay_requires_fresh_authority()
    {
        await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run); var committed = new NormalApprovalCommitted();
        await using var scope = new NormalApprovalScope(run, extra: committed); var deny = true;
        scope.Authority.Read = () => !deny || committed.Commits == 0;
        Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(id, period, "normal-release")).Status); scope.AssertClean(); await NormalSafetyDurable(run, id, true);
        Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(id, period, "normal-release")).Status);
        deny = false; var replay = await scope.Approve(id, period, "normal-release"); Assert.True(replay.IsReplay); Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
        Assert.Equal(AccountingApiStatus.Conflict, (await scope.Service.ApproveAsync(id, 1, Guid.NewGuid(), "normal-release")).Status);
        scope.AssertClean(); await NormalSafetyDurable(run, id, true);
    }

    [SqlServerFact]
    public async Task Normal_approval_terminal_version_receipt_is_atomic_and_never_replaced_by_current_success()
    {
        await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run); var save = new NormalApprovalCommandSave();
        await using var scope = new NormalApprovalScope(run, extra: save); var deny = true;
        scope.Authority.Read = () => !deny || !save.Saved;
        Assert.Equal(AccountingApiStatus.Denied, (await scope.Service.ApproveAsync(id, 9, period, "normal-stale-version")).Status);
        scope.AssertClean(); Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName=@n", ("@n", NormalJournalCommandNamesV1.ApproveDraft)));
        deny = false; var conflict = await scope.Service.ApproveAsync(id, 9, period, "normal-stale-version"); Assert.Equal(AccountingApiStatus.Conflict, conflict.Status);
        Assert.Equal(AccountingApiCodes.VersionConflict, conflict.Code); Assert.Equal(AccountingApiStatus.Ok, (await scope.Approve(id, period, "normal-good-version")).Status);
        var replay = await scope.Service.ApproveAsync(id, 9, period, "normal-stale-version"); Assert.True(replay.IsReplay); Assert.Equal(conflict.Code, replay.Code); Assert.Null(replay.Resource);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName=@n", ("@n", NormalJournalCommandNamesV1.ApproveDraft))); scope.AssertClean();
    }

    [SqlServerFact]
    public async Task Normal_approval_controlled_1205_before_actual_audit_write_discards_owned_drafts_and_retries_the_same_key_once()
    {
        await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run);
        await using var scope = new NormalApprovalScope(run, auditDeadlock: true);
        var result = await scope.Approve(id, period, "normal-audit-deadlock");
        Assert.Equal(AccountingApiStatus.Conflict, result.Status); Assert.Equal(AccountingStoreCodes.ConcurrentChange, result.Code);
        Assert.True(Assert.IsType<NormalAuditDeadlockWriter>(scope.Writer).Injected); scope.AssertClean(); await NormalSafetyDurable(run, id, false);
        var success = await scope.Approve(id, period, "normal-audit-deadlock"); var replay = await scope.Approve(id, period, "normal-audit-deadlock");
        Assert.Equal(AccountingApiStatus.Ok, success.Status); Assert.True(replay.IsReplay); scope.AssertClean(); await NormalSafetyDurable(run, id, true);
    }

    [SqlServerFact]
    public async Task Normal_approval_second_readonly_or_postcommit_1205_never_starts_another_native_mutation()
    {
        foreach (var postcommit in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var (id, period) = await NormalSafetyPrepare(run);
            var save = new NormalApprovalSave(id) { Fault = postcommit ? null : "concurrency" }; var committed = new NormalApprovalCommitted();
            await using var scope = new NormalApprovalScope(run, save, committed); var transactions = new HashSet<Guid>(); var injected = false;
            scope.Authority.BeforeRead = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (postcommit ? committed.Commits > 0 : transactions.Count == 2)
                { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return Task.CompletedTask;
            };
            Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Approve(id, period, "normal-no-second-loop")).Status);
            Assert.True(injected); Assert.Equal(postcommit ? 1 : 2, transactions.Count); Assert.Equal(1, save.FinalAttempts);
            scope.AssertClean(); await NormalSafetyDurable(run, id, postcommit);
        }
    }

    private static async Task<(Guid Draft, Guid Period)> NormalSafetyPrepare(Run run)
    {
        var period = await run.OpenSeptemberAsync(CompanyA); var draft = NormalDraft(run, "normal-owned-" + Guid.NewGuid().ToString("N"));
        await using var scope = run.Store(CompanyA); await scope.Drafts.CreateNormalDraftAsync(draft, _ => Task.CompletedTask, Metadata(run)); return (draft.Id, period);
    }
    private static async Task NormalSafetyDurable(Run run, Guid id, bool approved)
    {
        Assert.Equal(approved ? 2L : 1L, await run.ScalarAsync<long>("SELECT Version FROM accounting.NormalJournalDrafts WHERE Id=@id", ("@id", id)));
        Assert.Equal(approved ? 2 : 1, await run.ScalarAsync<int>("SELECT Status FROM accounting.NormalJournalDrafts WHERE Id=@id", ("@id", id)));
        Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE Id=@id", ("@id", id)));
        Assert.Equal(approved ? 2 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalLines WHERE JournalId=@id", ("@id", id)));
        Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName=@n", ("@n", NormalJournalCommandNamesV1.ApproveDraft)));
        if (approved) Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalLines j JOIN accounting.NormalJournalDraftLines d ON d.CompanyId=j.CompanyId AND d.DraftId=j.JournalId AND d.LineNumber=j.LineNumber WHERE j.JournalId=@id AND (j.AccountId<>d.AccountId OR j.Debit+j.Credit<>d.Amount)", ("@id", id)));
        Assert.Equal(approved ? 2 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType IN (N'accounting.normal-journal-draft.approved.v1',N'accounting.journal.posted.v1')", ("@id", id.ToString("D"))));
    }
    private static AccountingCommandRow NormalSafetyCaller() => new() { CompanyId = CompanyA, CommandName = "SENTETIK caller", IdempotencyKeyHash = "caller", ActorId = "caller", PayloadHash = "hash", ResultJson = "{}", CorrelationId = "corr" };
    private static AuditEventDraft NormalSafetyAudit(Run run) => new(CompanyA, "SENTETIK caller", "Accounting.NormalJournalDraft", "caller", "Approve", 1, 2,
        "User", run.Actor.ToString("D"), NormalJournalPermissionNamesV1.DraftApprove, [], "{}", "{}", "SENTETIK", null, AuditAiInvolvement.None, null, "Web", null, null, run.Correlation, null, "caller-key");

    private sealed class NormalApprovalScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public NormalApprovalCompany Company { get; } = new(); public NormalApprovalUser User { get; } public NormalApprovalAuthority Authority { get; } = new();
        public AccountingDbContext Db { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public NormalJournalDraftServiceV1 Service { get; }
        public NormalApprovalScope(Run run, NormalApprovalSave? save = null, IInterceptor? extra = null, bool missingWriter = false, bool auditDeadlock = false)
        {
            Company.Set(CompanyA); User = new(run.Actor); auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>();
            var actualWriter = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            Writer = auditDeadlock ? new NormalAuditDeadlockWriter(actualWriter) : actualWriter;
            var builder = new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) builder.AddInterceptors(save); if (extra is not null) builder.AddInterceptors(extra); if (extra is NormalApprovalCleanup cleanup) cleanup.Configure(builder);
            Db = new(builder.Options, Company); var core = new AccountingStore(Db, Company, Collector, TimeProvider.System,
                new AccountingAmountScaleVerifier(new SyntheticMoneyProfiles(CompanyA)), normalApprovalAuditWriter: missingWriter ? null : Writer);
            var binding = new NormalJournalPermissionBindingV1(); var correlation = new CorrelationContext(); correlation.Set(run.Correlation, null, "Web", null, null);
            Service = new(core.WithNormalDraftApprovalSafety(), core, new(binding, Authority, Company, User), binding, Authority, Company, User, correlation);
        }
        public Task<AccountingApiResult<NormalJournalDraftViewV1>> Approve(Guid id, Guid period, string key, CancellationToken token = default) => Service.ApproveAsync(id, 1, period, key, token);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class NormalApprovalCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class NormalApprovalUser(Guid actor) : ICurrentUser
    { public Guid Actor = actor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D");
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA }; public bool CanAccessCompany(Guid id) => Member && id == CompanyA; }
    private sealed class NormalApprovalAuthority : IErpAuthorityRevalidationService
    { public Func<bool>? Read; public Func<Task>? BeforeRead;
      public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
      { Assert.Equal(NormalJournalPermissionNamesV1.DraftApprove, permission); if (BeforeRead is not null) await BeforeRead(); return new(Read?.Invoke() ?? true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); } }
    private sealed class NormalAuditDeadlockWriter(IAuditTransactionWriterV1 actual) : IAuditTransactionWriterV1
    {
        public bool Injected; public bool HasPendingDrafts => actual.HasPendingDrafts;
        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken token = default)
        { if (!Injected) { Injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); } return actual.WritePendingAsync(transaction, token); }
        public void DiscardPending() => actual.DiscardPending();
    }
    private sealed class NormalApprovalSave(Guid id) : SaveChangesInterceptor
    {
        public string? Fault; public bool FinalSaved, Injected; public int FinalAttempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is AccountingDbContext db && db.ChangeTracker.Entries<NormalJournalDraftRow>().Any(x => x.Entity.Id == id && x.State == EntityState.Modified && x.Entity.Version == 2))
            {
                FinalAttempts++; if (Fault is not null)
                { Injected = true; if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                  if (Fault == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK normal approval race");
                  var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); if (Fault == "wrapped") throw new DbUpdateException("SENTETIK 1205", error); throw error; }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (FinalAttempts > 0) FinalSaved = true; return ValueTask.FromResult(result); }
    }
    private sealed class NormalApprovalCommandSave : SaveChangesInterceptor
    {
        public bool Saved;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class NormalApprovalCommitted : DbTransactionInterceptor
    { public int Commits; public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default) { Commits++; return Task.CompletedTask; } }
    private sealed class NormalApprovalCleanup(bool dispose) : DbTransactionInterceptor
    {
        public bool Armed, Failed;
        public void Configure(DbContextOptionsBuilder<AccountingDbContext> builder) => builder.LogTo((id, _) => id == RelationalEventId.TransactionDisposed,
            _ => { if (Armed && dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK dispose fault"); } });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (Armed && !dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK rollback fault"); } return ValueTask.FromResult(result); }
    }
}
