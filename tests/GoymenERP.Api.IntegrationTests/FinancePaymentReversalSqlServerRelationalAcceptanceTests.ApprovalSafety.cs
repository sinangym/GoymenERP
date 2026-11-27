using System.Data;
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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinancePaymentReversalSqlServerRelationalAcceptanceTests
{
    // Existing API-23 recorded-payment fixture is synthetic upstream data; proposals and decisions use actual native
    // commands, migrated Finance SQL and the real scoped Audit writer. Permission/delegation timing is controlled.
    [SqlServerFact]
    public async Task Native_payment_reversal_late_fences_roll_back_counter_event_barrier_receipt_and_audit_then_same_key_executes_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "before-audit", "after-audit", "after-save", "precommit", "actor", "company", "membership", "delegation" })
        {
            var (payment, proposal) = await DecisionSafetyPrepare(run); var save = new DecisionSafetySave(proposal);
            await using var scope = new DecisionSafetyScope(run, save: save);
            var deny = true; var seenPending = false; var observed = false; var savedFences = 0;
            scope.Permissions.Check = () =>
            {
                var staged = scope.Database.ChangeTracker.Entries<PaymentReversalProposalRow>().Any(e => e.Entity.Id == proposal && e.Entity.Version == 2);
                if (staged && scope.Writer.HasPendingDrafts) seenPending = true;
                if (save.Saved) savedFences++;
                // Save post-fence (1), wrapper post-execution (2), precommit (3). There is no receipt-query await in this safety increment.
                var reached = phase == "before-audit" ? staged && scope.Writer.HasPendingDrafts
                    : phase == "after-audit" ? staged && seenPending && !scope.Writer.HasPendingDrafts
                    : phase == "precommit" ? savedFences >= 3 : save.Saved;
                if (!deny || !reached) return PermissionDecision.Allowed;
                observed = true;
                if (phase == "actor") { scope.User.Actor = "SENTETIK-other"; return PermissionDecision.Allowed; }
                if (phase == "company") { scope.Company.Set(TestCompanyIds.CompanyB); return PermissionDecision.Allowed; }
                if (phase == "membership") { scope.User.Member = false; return PermissionDecision.Allowed; }
                return PermissionDecision.Denied;
            };
            var refused = await scope.Decide(proposal, approve, "reversal-late-" + proposal);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome); Assert.True(observed);
            if (phase == "precommit") Assert.Equal(3, savedFences);
            if (phase == "after-audit") { Assert.True(seenPending); Assert.False(save.Saved); }
            scope.AssertClean(); await DecisionSafetyDurable(run, payment, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
            deny = false; scope.User.Actor = Checker; scope.User.Member = true; scope.Company.Set(CompanyA); save.Saved = false;
            Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(proposal, approve, "reversal-late-" + proposal)).Outcome);
            scope.AssertClean(); await DecisionSafetyDurable(run, payment, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Native_payment_reversal_entry_preserves_borrowed_ambient_dirty_pending_and_await_created_caller_state()
    {
        await using var run = await Run.StartAsync(); var (payment, proposal) = await DecisionSafetyPrepare(run);
        foreach (var boundary in new[] { "missing-writer", "borrowed", "ambient", "dirty", "audit", "await-dirty", "await-audit" })
        {
            await using var scope = new DecisionSafetyScope(run, missingWriter: boundary == "missing-writer");
            await using var borrowed = boundary == "borrowed" ? await scope.Database.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            void AddCaller()
            {
                if (boundary is "dirty" or "await-dirty") scope.Database.Commands.Add(new() { CompanyId = CompanyA, CommandName = "SENTETIK-caller" });
                else scope.Collector.Enqueue(DecisionSafetyDraft(proposal));
            }
            if (boundary is "dirty" or "audit") AddCaller();
            if (boundary is "await-dirty" or "await-audit") scope.Permissions.AfterRead = () => { scope.Permissions.AfterRead = null; AddCaller(); return Task.CompletedTask; };
            var result = await scope.Decide(proposal, true, "reversal-entry-" + boundary);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
            Assert.Equal(boundary is "dirty" or "await-dirty", scope.Database.ChangeTracker.HasChanges());
            Assert.Equal(boundary is "audit" or "await-audit", scope.Writer.HasPendingDrafts);
            if (borrowed is not null) Assert.Same(borrowed, scope.Database.Database.CurrentTransaction);
        }
        await DecisionSafetyDurable(run, payment, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
    }

    [SqlServerFact]
    public async Task Native_payment_reversal_failed_save_or_controlled_1205_aborts_before_one_readonly_original_key_pass_without_mutation_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "concurrency", "direct-audit-1205", "wrapped-save-1205", "provider-aborted-1205" })
        foreach (var publishWinner in new[] { false, true })
        {
            var (payment, proposal) = await DecisionSafetyPrepare(run);
            var save = new DecisionSafetySave(proposal) { FailSave = phase is "concurrency" or "wrapped-save-1205", Deadlock = phase == "wrapped-save-1205" };
            await using var scope = new DecisionSafetyScope(run, save: save);
            var injected = false; var reconciled = false; var boundaries = new HashSet<Guid>(); var key = "reversal-race-" + proposal;
            PaymentReversalProposalCommandResult? winner = null;
            scope.Permissions.Check = () =>
            {
                if (!injected && phase == "direct-audit-1205" && scope.Writer.HasPendingDrafts)
                { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return PermissionDecision.Allowed;
            };
            scope.Permissions.AfterRead = async () =>
            {
                var tx = scope.Database.Database.CurrentTransaction;
                if (tx is not null) boundaries.Add(tx.TransactionId);
                if (!injected && phase == "provider-aborted-1205" && scope.Writer.HasPendingDrafts && tx is not null)
                {
                    var sql = Assert.IsType<SqlTransaction>(tx.GetDbTransaction()); await sql.RollbackAsync(); Assert.Null(sql.Connection);
                    injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                }
                if (reconciled || !(injected || save.Injected) || tx is not null) return;
                reconciled = true; scope.AssertClean();
                if (!publishWinner) return;
                // Ordered real native winner after the victim rollback, not a simultaneous SQL deadlock claim.
                await using var other = new DecisionSafetyScope(run); winner = await other.Decide(proposal, approve, key);
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome);
            };
            var result = await scope.Decide(proposal, approve, key);
            Assert.True(reconciled); Assert.Equal(2, boundaries.Count); Assert.Equal(phase is "concurrency" or "wrapped-save-1205" ? 1 : 0, save.Attempts);
            scope.AssertClean();
            if (publishWinner)
            {
                Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
                Assert.Equal(JsonSerializer.Serialize(winner.Resource, FinanceJson.CreateOptions()), JsonSerializer.Serialize(result.Resource, FinanceJson.CreateOptions()));
                Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId);
                await DecisionSafetyDurable(run, payment, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
            }
            else
            {
                Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.False(result.IsReplay); Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code);
                await DecisionSafetyDurable(run, payment, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
                save.FailSave = false;
                Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(proposal, approve, key)).Outcome);
                await DecisionSafetyDurable(run, payment, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
            }
        }
    }

    [SqlServerFact]
    public async Task Native_payment_reversal_cleanup_faults_clear_owned_work_and_never_start_a_winner_pass()
    {
        await using var run = await Run.StartAsync(); var (payment, proposal) = await DecisionSafetyPrepare(run);
        var rollback = new DecisionSafetyRollbackFault(); await using var scope = new DecisionSafetyScope(run, extra: rollback);
        var boundaries = new HashSet<Guid>(); scope.Permissions.Check = () =>
        {
            if (scope.Database.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId);
            if (scope.Writer.HasPendingDrafts) throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
            return PermissionDecision.Allowed;
        };
        var error = await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(proposal, true, "reversal-cleanup"));
        Assert.Contains(error.InnerExceptions, item => item is SqlException { Number: 1205 }); Assert.Equal(1, rollback.Attempts); Assert.Single(boundaries);
        scope.AssertClean(); await DecisionSafetyDurable(run, payment, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
    }

    [SqlServerFact]
    public async Task Native_payment_reversal_postcommit_release_refusal_keeps_original_receipt_and_replay_never_duplicates_money()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var (payment, proposal) = await DecisionSafetyPrepare(run); var committed = new DecisionSafetyCommitted();
            await using var scope = new DecisionSafetyScope(run, extra: committed);
            committed.AfterCommit = () => scope.User.Member = false; var key = "reversal-committed-" + proposal;
            Assert.Equal(CommandOutcome.Denied, (await scope.Decide(proposal, approve, key)).Outcome); Assert.Equal(1, committed.Commits);
            scope.AssertClean(); await DecisionSafetyDurable(run, payment, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
            committed.AfterCommit = null; scope.User.Member = true; scope.Correlation = "SENTETIK-retry";
            var result = await scope.Decide(proposal, approve, key); Assert.True(result.IsReplay); Assert.Equal(scope.OriginalCorrelation, result.OriginalCorrelationId);
            using var wire = JsonDocument.Parse(JsonSerializer.Serialize(result, FinanceJson.CreateOptions())); Assert.Equal(5, wire.RootElement.EnumerateObject().Count());
            Assert.Equal("2", result.Resource!.Version);
            scope.AssertClean(); await DecisionSafetyDurable(run, payment, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Native_payment_reversal_save_created_audit_ghost_is_discarded_and_terminal_version_conflict_receipt_replays_unchanged()
    {
        await using var run = await Run.StartAsync(); var (payment, proposal) = await DecisionSafetyPrepare(run);
        var save = new DecisionSafetySave(proposal); await using var scope = new DecisionSafetyScope(run, save: save);
        save.AfterSave = () => scope.Collector.Enqueue(DecisionSafetyDraft(proposal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Decide(proposal, true, "reversal-save-ghost"));
        scope.AssertClean(); await DecisionSafetyDurable(run, payment, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
        save.AfterSave = null;
        var conflict = await scope.Decide(proposal, true, "reversal-terminal-conflict", version: "9"); Assert.Equal(CommandOutcome.VersionConflict, conflict.Outcome);
        var replay = await scope.Decide(proposal, true, "reversal-terminal-conflict", version: "9"); Assert.True(replay.IsReplay); Assert.Equal(conflict.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(proposal, true, "reversal-save-ghost")).Outcome);
        scope.AssertClean(); await DecisionSafetyDurable(run, payment, proposal, 2, ReversalProposalStatuses.Approved, 2, 1, 1);
    }

    [SqlServerFact]
    public async Task Native_payment_reversal_second_readonly_fault_and_postcommit_1205_never_retry_mutation()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "readonly-second", "postcommit" })
        {
            var (payment, proposal) = await DecisionSafetyPrepare(run); var save = new DecisionSafetySave(proposal) { FailSave = phase == "readonly-second" };
            var committed = new DecisionSafetyCommitted(); await using var scope = new DecisionSafetyScope(run, save: save, extra: committed);
            var boundaries = new HashSet<Guid>(); var injected = false;
            scope.Permissions.Check = () =>
            {
                if (phase == "postcommit" && committed.Commits > 0) { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return PermissionDecision.Allowed;
            };
            scope.Permissions.AfterRead = () =>
            {
                if (scope.Database.Database.CurrentTransaction is { } tx)
                {
                    boundaries.Add(tx.TransactionId);
                    if (phase == "readonly-second" && boundaries.Count == 2) { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                }
                return Task.CompletedTask;
            };
            var key = "reversal-no-second-retry-" + proposal;
            Assert.Equal(CommandOutcome.Unavailable, (await scope.Decide(proposal, approve, key)).Outcome);
            Assert.True(injected); Assert.Equal(phase == "readonly-second" ? 2 : 1, boundaries.Count); Assert.Equal(1, save.Attempts); scope.AssertClean();
            if (phase == "readonly-second") await DecisionSafetyDurable(run, payment, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
            else
            {
                await DecisionSafetyDurable(run, payment, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
                scope.Permissions.Check = null;
                var replay = await scope.Decide(proposal, approve, key); Assert.True(replay.IsReplay); Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
                Assert.Equal(1, save.Attempts); scope.AssertClean(); await DecisionSafetyDurable(run, payment, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
            }
        }
    }

    [SqlServerFact]
    public async Task Two_real_guarded_payment_reversal_sessions_at_the_pre_audit_boundary_commit_one_complete_economic_outcome()
    {
        await using var run = await Run.StartAsync(); var (payment, proposal) = await DecisionSafetyPrepare(run);
        await using var first = new DecisionSafetyScope(run); await using var second = new DecisionSafetyScope(run);
        using var barrier = new Barrier(2); var firstWaited = false; var secondWaited = false;
        first.Permissions.AfterRead = async () =>
        {
            if (firstWaited || !first.Writer.HasPendingDrafts) return; firstWaited = true;
            await Task.Run(() => { if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30))) throw new TimeoutException("SENTETIK first native audit boundary"); });
        };
        second.Permissions.AfterRead = async () =>
        {
            if (secondWaited || !second.Writer.HasPendingDrafts) return; secondWaited = true;
            await Task.Run(() => { if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30))) throw new TimeoutException("SENTETIK second native audit boundary"); });
        };
        var one = first.Decide(proposal, true, "reversal-parallel-one"); var two = second.Decide(proposal, true, "reversal-parallel-two");
        var results = await Task.WhenAll(one, two);
        Assert.True(firstWaited); Assert.True(secondWaited); Assert.Single(results, r => r.Outcome == CommandOutcome.Succeeded);
        var loser = Assert.Single(results, r => r.Outcome != CommandOutcome.Succeeded);
        Assert.Contains(loser.Outcome, new[] { CommandOutcome.VersionConflict, CommandOutcome.Unavailable });
        first.AssertClean(); second.AssertClean();
        await DecisionSafetyDurable(run, payment, proposal, 2, ReversalProposalStatuses.Approved, 1, 1, 1);
    }

    private static async Task<(Guid Payment, Guid Proposal)> DecisionSafetyPrepare(Run run)
    {
        var payment = await run.RecordPaymentAsync("1250.50"); var result = await run.ProposeAsync(Maker, "safety-propose-" + payment, payment);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome); return (payment, result.Resource!.Id);
    }
    private static async Task DecisionSafetyDurable(Run run, Guid payment, Guid proposal, long version, int status, int commands, int audits, int events)
    {
        Assert.Equal(version, await run.ScalarAsync<long>("SELECT Version FROM finance.PaymentReversalProposals WHERE CompanyId=@c AND Id=@p", ("@c", CompanyA), ("@p", proposal)));
        Assert.Equal(status, await run.ScalarAsync<int>("SELECT Status FROM finance.PaymentReversalProposals WHERE CompanyId=@c AND Id=@p", ("@c", CompanyA), ("@p", proposal)));
        Assert.Equal(commands, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CompanyId=@c AND ManualBankPaymentId=@p AND CommandName IN (N'finance.payment-reversal.approve',N'finance.payment-reversal.reject')", ("@c", CompanyA), ("@p", payment)));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@c AND EntityId=@p AND EventType IN (N'finance.payment-reversal.approved.v1',N'finance.payment-reversal.rejected.v1')", ("@c", CompanyA), ("@p", proposal.ToString("D"))));
        Assert.Equal(events, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEventReversals WHERE CompanyId=@c AND ProposalId=@p", ("@c", CompanyA), ("@p", proposal)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ManualBankPayments WHERE CompanyId=@c AND Id=@p AND Version=3 AND Status=@status", ("@c", CompanyA), ("@p", payment), ("@status", ManualBankPaymentStatuses.Recorded)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents WHERE CompanyId=@c AND PaymentId=@p", ("@c", CompanyA), ("@p", payment)));
        Assert.Equal(events, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocationStates WHERE CompanyId=@c AND PaymentId=@p AND UnallocatedAmountText=N'0.00'", ("@c", CompanyA), ("@p", payment)));
    }
    private static AuditEventDraft DecisionSafetyDraft(Guid proposal) => new(CompanyA, "finance.payment-reversal.approved.v1", "Finance.PaymentReversalProposal", proposal.ToString("D"), "Approve", 1, 2,
        "User", Checker, FinancePermissionNames.PaymentReverseApprove, [], "{}", "{}", "SENTETIK caller draft", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK", null, "SENTETIK-key");
    private sealed class DecisionSafetyScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public DecisionSafetyCompany Company { get; } = new(); public DecisionSafetyUser User { get; } = new(); public DecisionSafetyPermissions Permissions { get; } = new();
        public FinanceDbContext Database { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public IPaymentReversalProposalService Service { get; } public string OriginalCorrelation { get; } = "reversal-safety-" + Guid.NewGuid().ToString("N"); public string Correlation { get; set; }
        public DecisionSafetyScope(Run run, DecisionSafetySave? save = null, IInterceptor? extra = null, bool missingWriter = false)
        {
            Correlation = OriginalCorrelation; Company.Set(CompanyA); auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company)); if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra);
            Database = new(options.Options, Company); var envelope = new FinanceAuditEnvelope(FinancePermissionNames.PaymentReverseApprove, [], "Web");
            Service = new PaymentReversalProposalService(Database, Company, User, Permissions, new OpenPeriod(), Collector, new(envelope, envelope), TimeProvider.System, missingWriter ? null : Writer).WithApprovalSafety();
        }
        public Task<PaymentReversalProposalCommandResult> Decide(Guid proposal, bool approve, string key, string version = "1") => approve
            ? Service.ApproveAsync(new(CompanyA, Checker, Correlation, null), new(key), new(proposal, version), default)
            : Service.RejectAsync(new(CompanyA, Checker, Correlation, null), new(key), new(proposal, version, " SENTETIK ret "), default);
        public void AssertClean() { Assert.False(Database.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Database.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Database.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class DecisionSafetyCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid value) => CompanyId = value; }
    private sealed class DecisionSafetyUser : ICurrentUser
    { public string Actor = Checker; public bool Member = true; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, TestCompanyIds.CompanyB }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class DecisionSafetyPermissions : IFinancePermissionPort
    { public Func<PermissionDecision>? Check; public Func<Task>? AfterRead;
      public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
      { var result = Check?.Invoke() ?? PermissionDecision.Allowed; if (AfterRead is not null) await AfterRead(); return result; } }
    private sealed class DecisionSafetySave(Guid proposal) : SaveChangesInterceptor
    {
        public bool Saved, FailSave, Deadlock, Injected; public int Attempts; public Action? AfterSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is FinanceDbContext db && db.ChangeTracker.Entries<PaymentReversalProposalRow>().Any(e => e.Entity.Id == proposal && e.State == EntityState.Modified))
            { Attempts++; if (FailSave) { Injected = true; if (Deadlock) throw new DbUpdateException("SENTETIK victim", SqlConcurrencyWrapperPolicyTests.SqlFailure(1205)); throw new DbUpdateConcurrencyException("SENTETIK race"); } }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) { Saved = true; AfterSave?.Invoke(); } return ValueTask.FromResult(result); }
    }
    private sealed class DecisionSafetyRollbackFault : DbTransactionInterceptor
    { public int Attempts; public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
      { Attempts++; throw new InvalidOperationException("SENTETIK rollback failure"); } }
    private sealed class DecisionSafetyCommitted : DbTransactionInterceptor
    { public int Commits; public Action? AfterCommit; public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default)
      { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; } }
}
