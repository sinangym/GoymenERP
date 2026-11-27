using System.Data.Common;
using System.Text.Json;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    // Actual SQL transaction and SAME scoped Audit collector/writer. Lender and permission answers remain controlled.
    [SqlServerFact]
    public async Task Bank_limit_owned_decisions_roll_back_late_authority_and_delegation_loss_then_same_key_decides_once()
    {
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "before-save", "after-audit", "after-save", "precommit", "actor", "company", "delegation" })
        {
            await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run);
            var save = new LimitDecisionSave(id); await using var scope = new LimitDecisionScope(run, save: save);
            var deny = true; var observed = false; var savedFences = 0; var sawPending = false;
            scope.Permission.Check = () =>
            {
                var staged = scope.Db.ChangeTracker.Entries<FinanceCreditLimitRow>().Any(x => x.Entity.Id == id && x.Entity.Version == 2);
                if (staged && scope.Writer.HasPendingDrafts) sawPending = true;
                if (save.Saved) savedFences++;
                var reached = phase == "before-save" ? staged && scope.Writer.HasPendingDrafts
                    : phase == "after-audit" ? staged && sawPending && !scope.Writer.HasPendingDrafts
                    : phase == "precommit" ? savedFences >= 5 : save.Saved;
                if (!deny || !reached) return PermissionDecision.Allowed;
                observed = true;
                if (phase == "actor") { scope.User.Actor = Maker; return PermissionDecision.Allowed; }
                if (phase == "company") { scope.Company.Set(CompanyB); return PermissionDecision.Allowed; }
                // Controlled delegation refusal is the same owner permission-port boundary used by Work's decorator.
                return PermissionDecision.Denied;
            };
            var key = "limit-late-" + phase; var result = await scope.Decide(id, key, approve);
            Assert.Equal(CommandOutcome.Denied, result.Outcome); Assert.True(observed); Assert.Equal(phase is not ("before-save" or "after-audit"), save.Saved);
            if (phase == "precommit") Assert.Equal(5, savedFences); // post-Save (1), receipt query before/after (2/3), post-native (4), precommit (5).
            scope.AssertClean(); await LimitSafetyDurable(run, id, null);
            deny = false; scope.User.Actor = Checker; scope.Company.Set(CompanyA); save.Saved = false;
            var success = await scope.Decide(id, key, approve); Assert.Equal(CommandOutcome.Succeeded, success.Outcome);
            Assert.Equal(approve ? CreditAccountStatus.Approved : CreditAccountStatus.Rejected, success.Resource!.Status);
            Assert.Equal(approve ? null : "SENTETIK literal ret", success.Resource.RejectionReason);
            Assert.Equal("2", success.Resource.Version); scope.AssertClean(); await LimitSafetyDurable(run, id, approve);
        }
    }

    [SqlServerFact]
    public async Task Bank_limit_decision_entry_preserves_borrowed_ambient_dirty_and_pending_caller_work()
    {
        foreach (var kind in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-writer" })
        {
            await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run);
            await using var scope = new LimitDecisionScope(run, missingWriter: kind == "missing-writer");
            using var ambient = kind == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            await using var borrowed = kind == "borrowed" ? await scope.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
            var caller = new FinanceCreditLimitRow { CompanyId = CompanyA, Id = Guid.NewGuid(), Version = 1, AmountText = "0", SourceJson = "{}", ProposedBy = "SENTETIK caller" };
            if (kind == "dirty") scope.Db.CreditLimits.Add(caller);
            if (kind == "pending") scope.Collector.Enqueue(LimitSafetyAudit(id));
            scope.Permission.Check = () =>
            {
                if (kind == "await-dirty" && !scope.Db.ChangeTracker.HasChanges()) scope.Db.CreditLimits.Add(caller);
                if (kind == "await-pending" && !scope.Writer.HasPendingDrafts) scope.Collector.Enqueue(LimitSafetyAudit(id));
                return PermissionDecision.Allowed;
            };
            Assert.Equal(CommandOutcome.Unavailable, (await scope.Decide(id, "limit-entry", true)).Outcome);
            Assert.Equal(kind is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges());
            if (kind is "dirty" or "await-dirty") Assert.Contains(scope.Db.ChangeTracker.Entries<FinanceCreditLimitRow>(), x => ReferenceEquals(x.Entity, caller) && x.State == EntityState.Added);
            Assert.Equal(kind is "pending" or "await-pending", scope.Writer.HasPendingDrafts);
            Assert.Same(borrowed, scope.Db.Database.CurrentTransaction);
            if (borrowed is not null) await borrowed.RollbackAsync();
            // The fixture owns this caller work; the refused command must NOT clear/discard it.
            scope.Db.ChangeTracker.Clear(); scope.Writer.DiscardPending(); await LimitSafetyDurable(run, id, null);
        }
    }

    [SqlServerFact]
    public async Task Bank_limit_failed_save_or_controlled_1205_cleans_owned_state_before_one_original_key_readonly_pass()
    {
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "audit-before-save", "completed-provider" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run);
            var save = new LimitDecisionSave(id) { Fault = fault }; await using var scope = new LimitDecisionScope(run, save: save);
            var key = "limit-race"; var winnerDone = false; CreditLimitResultV1? original = null;
            scope.Permission.AfterRead = async () =>
            {
                if (fault == "audit-before-save" && !save.Injected && scope.Writer.HasPendingDrafts)
                { save.Injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                if (!save.Injected || winnerDone || scope.Db.Database.CurrentTransaction is not null) return;
                winnerDone = true; scope.AssertClean();
                if (winner)
                {
                    await using var other = new LimitDecisionScope(run);
                    original = await other.Decide(id, key, true); Assert.Equal(CommandOutcome.Succeeded, original.Outcome); other.AssertClean();
                }
            };
            var answer = await scope.Decide(id, key, true); Assert.True(save.Injected); Assert.True(winnerDone);
            Assert.Equal(winner ? CommandOutcome.Succeeded : CommandOutcome.VersionConflict, answer.Outcome);
            Assert.Equal(winner, answer.IsReplay); Assert.Equal(fault == "audit-before-save" ? 0 : 1, save.Attempts);
            if (winner) { Assert.Equal(original!.Resource, answer.Resource); Assert.Equal(original.OriginalCorrelationId, answer.OriginalCorrelationId); }
            scope.AssertClean(); await LimitSafetyDurable(run, id, winner ? true : null);
            // Controlled SQL failures + an ordered native winner, NOT evidence of a simultaneous two-session deadlock.
        }
    }

    [SqlServerFact]
    public async Task Bank_limit_rollback_and_dispose_failures_still_clean_owned_state_and_never_read_a_winner()
    {
        foreach (var dispose in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run);
            var fault = new LimitDecisionCleanup(dispose); var save = new LimitDecisionSave(id) { Fault = "concurrency" };
            await using var scope = new LimitDecisionScope(run, save: save, cleanup: fault); var postAbortReads = 0;
            scope.Permission.Check = () => { if (save.Injected && scope.Db.Database.CurrentTransaction is null) postAbortReads++; return PermissionDecision.Allowed; };
            fault.Armed = true;
            await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(id, "limit-cleanup", true));
            Assert.True(fault.Failed); Assert.Equal(0, postAbortReads); Assert.Equal(1, save.Attempts); scope.AssertClean();
            await LimitSafetyDurable(run, id, null);
        }
    }

    [SqlServerFact]
    public async Task Bank_limit_postcommit_refusal_keeps_original_native_decision_and_replay_requires_fresh_authority()
    {
        await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run); var committed = new LimitDecisionCommitted();
        await using var scope = new LimitDecisionScope(run, extra: committed); var deny = true;
        scope.Permission.Check = () => deny && committed.Commits > 0 ? PermissionDecision.Denied : PermissionDecision.Allowed;
        Assert.Equal(CommandOutcome.Denied, (await scope.Decide(id, "limit-release", false)).Outcome);
        scope.AssertClean(); await LimitSafetyDurable(run, id, false);
        Assert.Equal(CommandOutcome.Denied, (await scope.Decide(id, "limit-release", false)).Outcome);
        deny = false; var replay = await scope.Decide(id, "limit-release", false);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay);
        Assert.Equal(scope.OriginalCorrelation, replay.OriginalCorrelationId); Assert.Equal("SENTETIK literal ret", replay.Resource!.RejectionReason);
        scope.AssertClean(); await LimitSafetyDurable(run, id, false);
    }

    [SqlServerFact]
    public async Task Bank_limit_second_readonly_fault_and_postcommit_1205_never_start_a_second_mutation()
    {
        foreach (var postcommit in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await LimitSafetyPrepare(run);
            var save = new LimitDecisionSave(id) { Fault = postcommit ? null : "concurrency" }; var committed = new LimitDecisionCommitted();
            await using var scope = new LimitDecisionScope(run, save: save, extra: committed); var transactions = new HashSet<Guid>(); var injected = false;
            scope.Permission.AfterRead = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (postcommit ? committed.Commits > 0 : transactions.Count == 2)
                { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return Task.CompletedTask;
            };
            Assert.Equal(CommandOutcome.Unavailable, (await scope.Decide(id, "limit-no-loop", true)).Outcome);
            Assert.True(injected); Assert.Equal(postcommit ? 1 : 2, transactions.Count); Assert.Equal(1, save.Attempts);
            scope.AssertClean(); await LimitSafetyDurable(run, id, postcommit ? true : null);
        }
    }

    private static async Task<Guid> LimitSafetyPrepare(Run run)
    {
        var account = await ApprovedMovementAccount(run);
        await using var maker = run.OpenLimit(Maker);
        var answer = await maker.Service.ProposeAsync(maker.Context, Key("limit-safety-propose"),
            new(account.Id, account.Version, null, null, "50.00", new("SENTETIK-LIMIT", account.StartDate, "1"), "SENTETIK source reason"), default);
        Assert.Equal(CommandOutcome.Succeeded, answer.Outcome); return answer.Resource!.Id;
    }
    private static async Task LimitSafetyDurable(Run run, Guid id, bool? approved)
    {
        Assert.Equal(approved is null ? 1L : 2L, await run.ScalarAsync<long>("SELECT Version FROM finance.CreditLimits WHERE CompanyId=@c AND Id=@id", ("@c", CompanyA), ("@id", id)));
        Assert.Equal(approved is null ? 1 : approved.Value ? 2 : 3, await run.ScalarAsync<int>("SELECT Status FROM finance.CreditLimits WHERE CompanyId=@c AND Id=@id", ("@c", CompanyA), ("@id", id)));
        Assert.Equal(approved is null ? 0 : 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands WHERE CompanyId=@c AND Command=N'finance.credit-limit.decide'", ("@c", CompanyA)));
        Assert.Equal(approved is null ? 0 : 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@c AND EntityType=N'Finance.CreditLimit' AND EntityId=@id AND EventType=N'finance.credit-limit.decide.v1'", ("@c", CompanyA), ("@id", id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovements"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }
    private static AuditEventDraft LimitSafetyAudit(Guid id) => new(CompanyA, "finance.credit-limit.decide.v1", "Finance.CreditLimit", id.ToString("D"), "Decide", 1, 2,
        "User", Checker, ErpPermissions.FinanceCreditsApprove, [], "{}", "{}", "SENTETIK caller", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK-limit-caller", null, "SENTETIK");
    private sealed class LimitDecisionScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public LimitDecisionCompany Company { get; } = new(); public LimitDecisionUser User { get; } = new(); public LimitDecisionPermission Permission { get; } = new();
        public FinanceDbContext Db { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public IFinanceCreditLimitServiceV1 Service { get; } public string OriginalCorrelation { get; } = "limit-safety-" + Guid.NewGuid().ToString("N");
        public LimitDecisionScope(Run run, LimitDecisionSave? save = null, IInterceptor? extra = null, LimitDecisionCleanup? cleanup = null, bool missingWriter = false)
        {
            Company.Set(CompanyA); auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra);
            if (cleanup is not null) { options.AddInterceptors(cleanup); cleanup.Configure(options); }
            Db = new(options.Options, Company);
            Service = new FinanceCreditLimitServiceV1(Db, Company, User, Permission, new SyntheticCreditSources(CompanyA), Collector,
                new(new(ErpPermissions.FinanceCreditsPropose, ["SENTETIK"], "Web"), new(ErpPermissions.FinanceCreditsApprove, ["SENTETIK"], "Web")),
                TimeProvider.System, missingWriter ? null : Writer).WithApprovalSafety();
        }
        public Task<CreditLimitResultV1> Decide(Guid id, string key, bool approve) => Service.DecideAsync(new(CompanyA, Checker, OriginalCorrelation, null), new(key), new(id, "1", approve, approve ? null : "SENTETIK literal ret"), default);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class LimitDecisionCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid value) => CompanyId = value; }
    private sealed class LimitDecisionUser : ICurrentUser
    { public string Actor = Checker; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA }; public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id); }
    private sealed class LimitDecisionPermission : IFinancePermissionPort
    { public Func<PermissionDecision>? Check; public Func<Task>? AfterRead;
      public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
      { Assert.Equal(FinanceAction.ApproveCredit, a); var answer = Check?.Invoke() ?? PermissionDecision.Allowed; if (AfterRead is not null) await AfterRead(); return answer; } }
    private sealed class LimitDecisionSave(Guid id) : SaveChangesInterceptor
    {
        public string? Fault; public bool Saved, Injected; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is FinanceDbContext db && db.ChangeTracker.Entries<FinanceCreditLimitRow>().Any(x => x.Entity.Id == id && x.State == EntityState.Modified))
            {
                Attempts++;
                if (Fault is not null)
                {
                    Injected = true;
                    if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                    if (Fault is "direct" or "completed-provider") throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (Fault == "wrapped") throw new DbUpdateException("SENTETIK 1205", SqlConcurrencyWrapperPolicyTests.SqlFailure(1205));
                    if (Fault == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK limit save race");
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class LimitDecisionCleanup(bool dispose) : DbTransactionInterceptor
    {
        public bool Armed, Failed;
        public void Configure(DbContextOptionsBuilder<FinanceDbContext> options) => options.LogTo((id, _) => id == RelationalEventId.TransactionDisposed,
            _ => { if (Armed && dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK dispose fault"); } });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (Armed && !dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK rollback fault"); } return ValueTask.FromResult(result); }
    }
    private sealed class LimitDecisionCommitted : DbTransactionInterceptor
    { public int Commits; public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default) { Commits++; return Task.CompletedTask; } }
}
