using System.Data.Common;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    // Existing native draft fixture plus real SQL and the SAME scoped Audit collector/writer.
    // Permission/delegation timings, Settings and treasury ports stay controlled; no granted-user/E2E claim.
    [SqlServerFact]
    public async Task Native_expense_approval_late_authority_and_delegation_refusal_roll_back_draft_attempt_and_audit_then_same_key_saves_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "before-save", "after-audit", "after-save", "precommit", "actor", "company", "membership", "delegation" })
        {
            var id = await ApprovalSafetyPrepare(run); var save = new ExpenseApprovalSave(id); await using var scope = new ExpenseApprovalScope(run, save: save);
            var deny = true; var observed = false; var savedFences = 0; var sawPending = false;
            scope.Permission.Check = () =>
            {
                var staged = scope.Db.ChangeTracker.Entries<ExpensePaymentRow>().Any(e => e.Entity.Id == id && e.Entity.Version == 2);
                if (staged && scope.Writer.HasPendingDrafts) sawPending = true;
                if (save.Saved) savedFences++;
                // Save post-fence (1), receipt before/after Commands (2/3), wrapper post-native (4), immediately before Commit (5).
                var reached = phase == "before-save" ? staged && scope.Writer.HasPendingDrafts
                    : phase == "after-audit" ? staged && sawPending && !scope.Writer.HasPendingDrafts
                    : phase == "precommit" ? savedFences >= 5 : save.Saved;
                if (!deny || !reached) return PermissionDecision.Allowed;
                observed = true;
                if (phase == "actor") { scope.User.Actor = "SENTETIK-other"; return PermissionDecision.Allowed; }
                if (phase == "company") { scope.Company.Set(CompanyB); return PermissionDecision.Allowed; }
                if (phase == "membership") { scope.User.Member = false; return PermissionDecision.Allowed; }
                return PermissionDecision.Denied;
            };
            var key = "expense-late-" + id;
            Assert.Equal(CommandOutcome.Denied, (await scope.Approve(id, key)).Outcome); Assert.True(observed);
            if (phase == "precommit") Assert.Equal(5, savedFences);
            if (phase is "before-save" or "after-audit") Assert.False(save.Saved);
            scope.AssertClean(); await ApprovalSafetyDurable(run, id, false, 0);
            deny = false; scope.User.Actor = Approver; scope.User.Member = true; scope.Company.Set(CompanyA); save.Saved = false;
            var result = await scope.Approve(id, key);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome); Assert.Equal(ExpensePaymentStatus.Approved, result.Resource!.Status);
            Assert.False(result.Resource.PaymentConfirmed); Assert.Equal("2", result.Resource.Version);
            scope.AssertClean(); await ApprovalSafetyDurable(run, id, true, 1);
        }
    }

    [SqlServerFact]
    public async Task Native_expense_approval_entry_preserves_borrowed_ambient_dirty_pending_audit_and_work_created_during_fresh_authority()
    {
        await using var run = await Run.StartAsync(); var id = await ApprovalSafetyPrepare(run);
        foreach (var boundary in new[] { "missing-writer", "borrowed", "ambient", "dirty", "audit", "await-dirty", "await-audit" })
        {
            await using var scope = new ExpenseApprovalScope(run, missingWriter: boundary == "missing-writer");
            await using var borrowed = boundary == "borrowed" ? await scope.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            void AddCaller()
            {
                if (boundary is "dirty" or "await-dirty") scope.Db.ExpensePaymentCommands.Add(new() { CompanyId = CompanyA, Id = Guid.NewGuid(), CommandName = "SENTETIK-caller" });
                else scope.Collector.Enqueue(ExpenseApprovalDraft(id));
            }
            if (boundary is "dirty" or "audit") AddCaller();
            if (boundary is "await-dirty" or "await-audit") scope.Permission.AfterRead = () => { scope.Permission.AfterRead = null; AddCaller(); return Task.CompletedTask; };
            Assert.Equal(CommandOutcome.Unavailable, (await scope.Approve(id, "expense-entry-" + boundary)).Outcome);
            Assert.Equal(boundary is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges());
            Assert.Equal(boundary is "audit" or "await-audit", scope.Writer.HasPendingDrafts);
            if (borrowed is not null) Assert.Same(borrowed, scope.Db.Database.CurrentTransaction);
        }
        await ApprovalSafetyDurable(run, id, false, 0);
    }

    [SqlServerFact]
    public async Task Native_expense_approval_failed_save_or_controlled_1205_cleans_owned_state_before_one_original_key_readonly_winner_pass()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "save-concurrency", "wrapped-save-1205", "direct-audit-1205", "provider-aborted-1205" })
        foreach (var hasWinner in new[] { false, true })
        {
            var id = await ApprovalSafetyPrepare(run); var key = "expense-winner-" + id;
            var save = new ExpenseApprovalSave(id) { Fail = phase is "save-concurrency" or "wrapped-save-1205", Deadlock = phase == "wrapped-save-1205" };
            await using var scope = new ExpenseApprovalScope(run, save: save);
            var injected = false; var reconciled = false; var boundaries = new HashSet<Guid>(); ExpensePaymentCommandResult? winner = null;
            scope.Permission.Check = () =>
            {
                if (!injected && phase == "direct-audit-1205" && scope.Writer.HasPendingDrafts)
                { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return PermissionDecision.Allowed;
            };
            scope.Permission.AfterRead = async () =>
            {
                var tx = scope.Db.Database.CurrentTransaction; if (tx is not null) boundaries.Add(tx.TransactionId);
                if (!injected && phase == "provider-aborted-1205" && scope.Writer.HasPendingDrafts && tx is not null)
                {
                    var sql = Assert.IsType<SqlTransaction>(tx.GetDbTransaction()); await sql.RollbackAsync(); Assert.Null(sql.Connection);
                    injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                }
                if (reconciled || !(injected || save.Injected) || tx is not null) return;
                reconciled = true; scope.AssertClean();
                if (!hasWinner) return;
                // Ordered actual native winner AFTER owned abort, not a real simultaneous deadlock claim.
                await using var other = new ExpenseApprovalScope(run); winner = await other.Approve(id, key);
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome);
            };
            var result = await scope.Approve(id, key);
            Assert.True(reconciled); Assert.Equal(2, boundaries.Count); Assert.Equal(phase is "save-concurrency" or "wrapped-save-1205" ? 1 : 0, save.Attempts);
            scope.AssertClean();
            if (hasWinner)
            {
                Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
                Assert.Equal(winner.Resource, result.Resource); Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId);
                await ApprovalSafetyDurable(run, id, true, 1);
            }
            else
            {
                Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.False(result.IsReplay);
                Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code);
                await ApprovalSafetyDurable(run, id, false, 0);
                save.Fail = false;
                Assert.Equal(CommandOutcome.Succeeded, (await scope.Approve(id, key)).Outcome);
                await ApprovalSafetyDurable(run, id, true, 1);
            }
        }
    }

    [SqlServerFact]
    public async Task Native_expense_approval_rollback_or_dispose_failure_still_cleans_owned_tracker_and_audit_without_winner_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var dispose in new[] { false, true })
        {
            var id = await ApprovalSafetyPrepare(run); var fault = new ExpenseApprovalCleanupFault(dispose);
            var save = new ExpenseApprovalSave(id) { Fail = true }; await using var scope = new ExpenseApprovalScope(run, save: save, cleanup: fault);
            var boundaries = new HashSet<Guid>(); scope.Permission.Check = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId);
                return PermissionDecision.Allowed;
            };
            fault.Armed = true;
            await Assert.ThrowsAsync<AggregateException>(() => scope.Approve(id, "expense-cleanup-" + id));
            Assert.True(fault.Failed); Assert.Single(boundaries); Assert.Equal(1, save.Attempts);
            Assert.False(scope.Db.ChangeTracker.HasChanges()); Assert.False(scope.Writer.HasPendingDrafts);
            await ApprovalSafetyDurable(run, id, false, 0);
        }
    }

    [SqlServerFact]
    public async Task Native_expense_approval_release_refusal_keeps_the_original_receipt_and_does_not_record_money()
    {
        await using var run = await Run.StartAsync(); var id = await ApprovalSafetyPrepare(run); var committed = new ExpenseApprovalCommitted();
        await using var scope = new ExpenseApprovalScope(run, extra: committed); var key = "expense-committed-" + id;
        committed.AfterCommit = () => scope.User.Member = false;
        Assert.Equal(CommandOutcome.Denied, (await scope.Approve(id, key)).Outcome); Assert.Equal(1, committed.Commits);
        scope.AssertClean(); await ApprovalSafetyDurable(run, id, true, 1);
        committed.AfterCommit = null; scope.User.Member = true; scope.Correlation = "SENTETIK-original-key-retry";
        var replay = await scope.Approve(id, key); Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay);
        Assert.Equal(scope.OriginalCorrelation, replay.OriginalCorrelationId); Assert.Equal("2", replay.Resource!.Version);
        Assert.False(replay.Resource.PaymentConfirmed); scope.AssertClean(); await ApprovalSafetyDurable(run, id, true, 1);
    }

    [SqlServerFact]
    public async Task Native_expense_approval_terminal_version_refusal_and_postsave_ghost_audit_remain_atomic()
    {
        await using var run = await Run.StartAsync(); var id = await ApprovalSafetyPrepare(run); var save = new ExpenseApprovalSave(id);
        await using var scope = new ExpenseApprovalScope(run, save: save);
        save.AfterSave = () => scope.Collector.Enqueue(ExpenseApprovalDraft(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Approve(id, "expense-audit-ghost"));
        scope.AssertClean(); await ApprovalSafetyDurable(run, id, false, 0); save.AfterSave = null;
        var stale = await scope.Approve(id, "expense-stale", "7");
        Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome); Assert.False(stale.IsReplay);
        var replay = await scope.Approve(id, "expense-stale", "7"); Assert.True(replay.IsReplay);
        Assert.Equal(stale.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Null(replay.Resource);
        scope.AssertClean(); await ApprovalSafetyDurable(run, id, false, 1);
        Assert.Equal(CommandOutcome.Succeeded, (await scope.Approve(id, "expense-audit-ghost")).Outcome);
        scope.AssertClean(); await ApprovalSafetyDurable(run, id, true, 2);
    }

    [SqlServerFact]
    public async Task Native_expense_approval_second_readonly_fault_and_postcommit_1205_never_start_another_mutation_attempt()
    {
        await using var run = await Run.StartAsync();
        foreach (var postcommit in new[] { false, true })
        {
            var id = await ApprovalSafetyPrepare(run); var save = new ExpenseApprovalSave(id) { Fail = !postcommit }; var committed = new ExpenseApprovalCommitted();
            await using var scope = new ExpenseApprovalScope(run, save: save, extra: committed);
            var boundaries = new HashSet<Guid>(); var injected = false;
            scope.Permission.Check = () =>
            {
                if (postcommit && committed.Commits > 0) { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return PermissionDecision.Allowed;
            };
            scope.Permission.AfterRead = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx)
                {
                    boundaries.Add(tx.TransactionId);
                    if (!postcommit && boundaries.Count == 2) { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                }
                return Task.CompletedTask;
            };
            Assert.Equal(CommandOutcome.Unavailable, (await scope.Approve(id, "expense-no-loop-" + id)).Outcome);
            Assert.True(injected); Assert.Equal(postcommit ? 1 : 2, boundaries.Count); Assert.Equal(1, save.Attempts);
            scope.AssertClean(); await ApprovalSafetyDurable(run, id, postcommit, postcommit ? 1 : 0);
        }
    }

    private static async Task<Guid> ApprovalSafetyPrepare(Run run)
    {
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK expense safety bank");
        return (await run.DraftAsync(CompanyA, bank, "expense-safety-draft-" + bank)).Id;
    }
    private static async Task ApprovalSafetyDurable(Run run, Guid id, bool approved, int attempts)
    {
        Assert.Equal(approved ? 2L : 1L, await run.ScalarAsync<long>("SELECT Version FROM finance.ExpensePayments WHERE CompanyId=@c AND Id=@p", ("@c", CompanyA), ("@p", id)));
        Assert.Equal(approved ? ExpensePaymentStatuses.Approved : ExpensePaymentStatuses.Draft,
            await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE CompanyId=@c AND Id=@p", ("@c", CompanyA), ("@p", id)));
        Assert.Equal(attempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CompanyId=@c AND PaymentId=@p AND CommandName=@n",
            ("@c", CompanyA), ("@p", id), ("@n", ExpensePaymentService.ApproveCommand)));
        Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@c AND EntityId=@p AND EventType=N'finance.expense-payment.approved.v1'", ("@c", CompanyA), ("@p", id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePayments WHERE CompanyId=@c AND Id=@p AND (Status=@s OR PaymentConfirmed=1 OR RecordedAtUtc IS NOT NULL)", ("@c", CompanyA), ("@p", id), ("@s", ExpensePaymentStatuses.Recorded)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
    }
    private static AuditEventDraft ExpenseApprovalDraft(Guid id) => new(CompanyA, "finance.expense-payment.approved.v1", "Finance.ExpensePayment", id.ToString("D"), "Approve", 1, 2,
        "User", Approver, FinancePermissionNames.ExpensePaymentApprove, [], "{}", "{}", "SENTETIK caller draft", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK", null, "SENTETIK-key");

    private sealed class ExpenseApprovalScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public ExpenseApprovalCompany Company { get; } = new(); public ExpenseApprovalUser User { get; } = new(); public ExpenseApprovalPermission Permission { get; } = new();
        public FinanceDbContext Db { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public IExpensePaymentService Service { get; } public string OriginalCorrelation { get; } = "expense-safety-" + Guid.NewGuid().ToString("N"); public string Correlation { get; set; }
        public ExpenseApprovalScope(Run run, ExpenseApprovalSave? save = null, IInterceptor? extra = null, ExpenseApprovalCleanupFault? cleanup = null, bool missingWriter = false)
        {
            Correlation = OriginalCorrelation; Company.Set(CompanyA); auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra);
            if (cleanup is not null) { options.AddInterceptors(cleanup); cleanup.Configure(options); }
            Db = new(options.Options, Company);
            Service = new ExpensePaymentService(Db, Company, User, Permission, new SingleExpenseItemCatalog(), new SyntheticBalance(TreasuryBalanceOutcome.Sufficient),
                new SyntheticSettings(CompanyA), new SyntheticOpenPeriod(), Collector, new(FinanceMoneyOperations.ExpensePaymentV1),
                new(new(FinancePermissionNames.ExpensePaymentDraftSave, [], "Web"), new(FinancePermissionNames.ExpensePaymentApprove, [], "Web"), new(FinancePermissionNames.ExpensePaymentRecord, [], "Web")),
                TimeProvider.System, missingWriter ? null : Writer, new SyntheticFinanceRecordNumbers()).WithApprovalSafety();
        }
        public Task<ExpensePaymentCommandResult> Approve(Guid id, string key, string version = "1") => Service.ApproveAsync(new(CompanyA, Approver, Correlation, null), new(key), new(id, version), default);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class ExpenseApprovalCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class ExpenseApprovalUser : ICurrentUser
    { public string Actor = Approver; public bool Member = true; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class ExpenseApprovalPermission : IFinancePermissionPort
    { public Func<PermissionDecision>? Check; public Func<Task>? AfterRead;
      public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken token)
      { var result = Check?.Invoke() ?? PermissionDecision.Allowed; if (AfterRead is not null) await AfterRead(); return result; } }
    private sealed class ExpenseApprovalSave(Guid id) : SaveChangesInterceptor
    {
        public bool Saved, Fail, Deadlock, Injected; public int Attempts; public Action? AfterSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is FinanceDbContext db && db.ChangeTracker.Entries<ExpensePaymentRow>().Any(e => e.Entity.Id == id && e.State == EntityState.Modified))
            { Attempts++; if (Fail) { Injected = true; if (Deadlock) throw new DbUpdateException("SENTETIK deadlock victim", SqlConcurrencyWrapperPolicyTests.SqlFailure(1205)); throw new DbUpdateConcurrencyException("SENTETIK expense save race"); } }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) { Saved = true; AfterSave?.Invoke(); } return ValueTask.FromResult(result); }
    }
    private sealed class ExpenseApprovalCleanupFault(bool dispose) : DbTransactionInterceptor
    {
        public bool Armed, Failed;
        // Existing real-provider diagnostics pattern: EF has a rollback interceptor but no dispose override.
        public void Configure(DbContextOptionsBuilder<FinanceDbContext> options) => options.LogTo(
            (id, _) => id == RelationalEventId.TransactionDisposed,
            _ => { if (Armed && dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK transaction disposed fault"); } });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (Armed && !dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK rollback fault"); } return ValueTask.FromResult(result); }
    }
    private sealed class ExpenseApprovalCommitted : DbTransactionInterceptor
    { public int Commits; public Action? AfterCommit;
      public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default)
      { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; } }
}
