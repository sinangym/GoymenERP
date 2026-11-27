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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditLimitSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Credit_limit_decisions_late_permission_company_actor_membership_or_delegation_loss_rollback_then_retry_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var fault in new[] { "before-audit", "after-audit", "after-save", "precommit", "actor", "company", "membership", "delegation" })
        {
            var id = await LimitSafetyPrepare(run); var probe = new LimitSafetySaveProbe(id);
            await using var scope = new LimitSafetyScope(run, probe: probe);
            var deny = true; var observed = false; var savedReads = 0;
            scope.Permissions.BeforeRead = () =>
            {
                var staged = scope.Database.ChangeTracker.Entries<PartyCreditLimitRow>().Any(row => row.Entity.Id == id && row.Entity.Version == 2);
                if (probe.Saved) savedReads++;
                // Post-save, receipt pre/post-read and wrapper post-execute fences precede the fifth (precommit) read.
                var phase = fault == "before-audit" ? staged && scope.Writer.HasPendingDrafts
                    : fault == "after-audit" ? staged && !scope.Writer.HasPendingDrafts
                    : fault == "precommit" ? savedReads >= 5 : probe.Saved;
                if (!deny || !phase) return PermissionDecision.Allowed;
                observed = true;
                if (fault == "actor") { scope.User.Actor = "SENTETIK-changed"; return PermissionDecision.Allowed; }
                if (fault == "company") { scope.Company.Set(CompanyB); return PermissionDecision.Allowed; }
                if (fault == "membership") { scope.User.Member = false; return PermissionDecision.Allowed; }
                // Controlled owner-port delegation denial, not a full Identity/WP3 acceptance claim.
                return PermissionDecision.Denied;
            };
            var refused = await scope.Decide(id, approve, "limit-late-" + fault);
            Assert.True(observed); Assert.Equal(CommandOutcome.Denied, refused.Outcome); scope.AssertClean();
            await LimitSafetyDurable(run, id, 1, 1, 0, 0);
            deny = false; scope.User.Actor = Approver; scope.User.Member = true; scope.Company.Set(CompanyA);
            probe.Saved = false;
            var stored = await scope.Decide(id, approve, "limit-late-" + fault);
            Assert.Equal(CommandOutcome.Succeeded, stored.Outcome); Assert.False(stored.IsReplay);
            var replay = await scope.Decide(id, approve, "limit-late-" + fault);
            Assert.True(replay.IsReplay); Assert.Equal(stored.Resource, replay.Resource);
            Assert.Equal(stored.OriginalCorrelationId, replay.OriginalCorrelationId); scope.AssertClean();
            await LimitSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    [SqlServerFact]
    public async Task Credit_limit_decision_entry_refusals_preserve_borrowed_ambient_dirty_and_pending_audit_even_for_replay()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await LimitSafetyPrepare(run);
            await using (var first = new LimitSafetyScope(run))
                Assert.Equal(CommandOutcome.Succeeded, (await first.Decide(id, approve, "limit-entry-original")).Outcome);
            foreach (var boundary in new[] { "missing-writer", "borrowed", "ambient", "dirty", "audit", "await-dirty", "await-audit" })
            foreach (var replay in new[] { false, true })
            {
                await using var scope = new LimitSafetyScope(run, missingWriter: boundary == "missing-writer");
                await using var borrowed = boundary == "borrowed" ? await scope.Database.Database.BeginTransactionAsync() : null;
                var row = boundary == "dirty" ? await scope.Database.PartyCreditLimits.SingleAsync(item => item.Id == id) : null;
                if (row is not null) row.UpdatedAtUtc = row.UpdatedAtUtc.AddSeconds(1);
                if (boundary == "audit") scope.Collector.Enqueue(LimitSafetyDraft(id));
                var injected = false;
                scope.Permissions.AfterRead = () =>
                {
                    if (!injected && boundary == "await-dirty")
                        scope.Database.PartyCreditLimits.Add(new PartyCreditLimitRow { Id = Guid.NewGuid(), CompanyId = CompanyA });
                    if (!injected && boundary == "await-audit") scope.Collector.Enqueue(LimitSafetyDraft(id));
                    injected = true; return Task.CompletedTask;
                };
                using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
                var ambientBefore = Transaction.Current;
                var result = await scope.Decide(id, approve, replay ? "limit-entry-original" : "limit-entry-new");
                Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Same(ambientBefore, Transaction.Current);
                Assert.Same(borrowed, scope.Database.Database.CurrentTransaction);
                Assert.Equal(boundary is "dirty" or "await-dirty", scope.Database.ChangeTracker.HasChanges());
                Assert.Equal(boundary is "audit" or "await-audit", scope.Writer.HasPendingDrafts);
                if (row is not null) Assert.Same(row, Assert.Single(scope.Database.ChangeTracker.Entries()).Entity);
                scope.Writer.DiscardPending();
            }
            await LimitSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    [SqlServerFact]
    public async Task Credit_limit_failed_save_discards_own_audit_before_one_original_winner_read_and_never_retries_mutation()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var publishWinner in new[] { false, true })
        {
            var id = await LimitSafetyPrepare(run); var probe = new LimitSafetySaveProbe(id) { FailSave = true };
            var key = "limit-race-key-" + id;
            await using var scope = new LimitSafetyScope(run, probe: probe);
            probe.BeforeFailure = () => scope.Collector.Enqueue(LimitSafetyDraft(id));
            var boundaries = new HashSet<Guid>(); PartyCreditLimitCommandResult? winner = null;
            scope.Permissions.AfterRead = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is not { } tx) return;
                boundaries.Add(tx.TransactionId);
                if (boundaries.Count != 2 || winner is not null || !publishWinner) return;
                Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
                Assert.Equal(1, probe.Attempts);
                await using var actual = new LimitSafetyScope(run);
                winner = await actual.Decide(id, approve, key);
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); Assert.False(winner.IsReplay);
            };
            var result = await scope.Decide(id, approve, key);
            Assert.Equal(2, boundaries.Count); Assert.Equal(1, probe.Attempts); scope.AssertClean();
            if (publishWinner)
            { Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(winner.Resource, result.Resource); Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId); }
            else
            { Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code); }
            await LimitSafetyDurable(run, id, publishWinner ? 2 : 1, publishWinner ? approve ? 2 : 3 : 1, publishWinner ? 1 : 0, publishWinner ? 1 : 0);
            if (!publishWinner)
            {
                // The unpersisted conflict did not reserve the original command/key.
                probe.FailSave = false;
                Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, approve, key)).Outcome);
                Assert.Equal(2, probe.Attempts);
                await LimitSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            }
        }
    }

    [SqlServerFact]
    public async Task Credit_limit_rollback_failure_still_disposes_and_discards_only_owned_work_without_winner_replay()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await LimitSafetyPrepare(run); var probe = new LimitSafetySaveProbe(id) { FailSave = true };
            var rollback = new LimitSafetyRollbackFault();
            await using var scope = new LimitSafetyScope(run, probe: probe, extra: rollback);
            probe.BeforeFailure = () => scope.Collector.Enqueue(LimitSafetyDraft(id));
            var boundaries = new HashSet<Guid>();
            scope.Permissions.AfterRead = () =>
            { if (scope.Database.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId); return Task.CompletedTask; };
            var failure = await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(id, approve, "limit-rollback-fault"));
            Assert.Contains(failure.InnerExceptions, error => error is InvalidOperationException && error.Message == "SENTETIK limit rollback fault");
            Assert.Equal(1, rollback.Attempts); Assert.Single(boundaries); Assert.Equal(1, probe.Attempts); scope.AssertClean();
            await LimitSafetyDurable(run, id, 1, 1, 0, 0);
            // End this synthetic account's pending proposal through the native command before the next case.
            await using var cleanup = new LimitSafetyScope(run);
            Assert.Equal(CommandOutcome.Succeeded, (await cleanup.Decide(id, false, "limit-cleanup-" + id)).Outcome);
        }
    }

    [SqlServerFact]
    public async Task Credit_limit_audit_draft_created_during_Save_cannot_escape_the_owned_boundary_or_poison_the_next_request()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await LimitSafetyPrepare(run); var probe = new LimitSafetySaveProbe(id);
            await using var scope = new LimitSafetyScope(run, probe: probe);
            probe.AfterSave = () => scope.Collector.Enqueue(LimitSafetyDraft(id));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Decide(id, approve, "limit-save-draft"));
            Assert.Equal("Credit limit decision has unflushed owner work.", error.Message); scope.AssertClean();
            await LimitSafetyDurable(run, id, 1, 1, 0, 0);
            probe.AfterSave = null;
            Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, approve, "limit-save-draft")).Outcome);
            await LimitSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
        }
    }

    [SqlServerFact]
    public async Task Credit_limit_commit_then_authority_loss_suppresses_release_but_keeps_original_decision_and_replay()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await LimitSafetyPrepare(run); var committed = new LimitSafetyCommitted();
            await using var scope = new LimitSafetyScope(run, extra: committed);
            committed.AfterCommit = () => scope.User.Member = false;
            var denied = await scope.Decide(id, approve, "limit-committed-original");
            Assert.Equal(CommandOutcome.Denied, denied.Outcome); Assert.Equal(1, committed.Commits); scope.AssertClean();
            await LimitSafetyDurable(run, id, 2, approve ? 2 : 3, 1, 1);
            var originalJson = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.PartyCreditLimitCommands WHERE PartyCreditLimitId=@id AND CommandName=@command",
                ("@id", id), ("@command", approve ? FinancePartyCreditLimitService.ApproveCommand : FinancePartyCreditLimitService.RejectCommand));
            // Later native direct Set supersedes an approved record; the original receipt must still say Approved/version2.
            await using (var direct = run.Open(CompanyA, Approver))
            {
                var revision = await run.ScalarAsync<long>("SELECT COALESCE(MAX(AccountRevision),CONVERT(bigint,0)) FROM finance.PartyCreditLimits WHERE CompanyId=@company AND CompanyAccountId=@account", ("@company", CompanyA), ("@account", Account));
                Assert.Equal(CommandOutcome.Succeeded, (await direct.Limits.SetAsync(direct.Context, Key("limit-later-" + id),
                    new(Party, Account, Currency, "55.50", revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), default)).Outcome);
            }
            scope.User.Member = true; scope.Correlation = "SENTETIK-new-correlation";
            var replay = await scope.Decide(id, approve, "limit-committed-original");
            Assert.True(replay.IsReplay); Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
            Assert.Equal("2", replay.Resource!.Version); Assert.Equal(approve ? PartyCreditLimitStatus.Approved : PartyCreditLimitStatus.Rejected, replay.Resource.Status);
            Assert.NotEqual(scope.Correlation, replay.OriginalCorrelationId); Assert.Equal(scope.OriginalCorrelation, replay.OriginalCorrelationId);
            Assert.Equal(originalJson, await run.ScalarAsync<string>("SELECT ResultJson FROM finance.PartyCreditLimitCommands WHERE PartyCreditLimitId=@id AND CommandName=@command",
                ("@id", id), ("@command", approve ? FinancePartyCreditLimitService.ApproveCommand : FinancePartyCreditLimitService.RejectCommand)));
            await using (var otherActor = new LimitSafetyScope(run))
            {
                otherActor.User.Actor = SecondApprover;
                var conflict = await otherActor.Decide(id, approve, "limit-committed-original", actor: SecondApprover);
                Assert.Equal(CommandOutcome.IdempotencyConflict, conflict.Outcome); Assert.Null(conflict.Resource);
                otherActor.AssertClean();
            }
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event", ("@id", id.ToString("D")), ("@event", approve ? "finance.credit-limit.approved.v1" : "finance.credit-limit.rejected.v1")));
            scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Credit_limit_native_maker_foreign_company_stale_and_reason_rules_survive_guarded_composition()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var id = await LimitSafetyPrepare(run);
            await using (var maker = new LimitSafetyScope(run))
            {
                maker.User.Actor = Proposer;
                Assert.Equal(CommandOutcome.Denied, (await maker.Decide(id, approve, "limit-maker", actor: Proposer)).Outcome);
                maker.AssertClean();
            }
            await using (var foreign = new LimitSafetyScope(run, company: CompanyB))
            {
                var denied = await foreign.Decide(id, approve, "limit-foreign");
                Assert.Equal(CommandOutcome.ValidationFailed, denied.Outcome); Assert.Null(denied.Resource);
                Assert.Equal("credit_limit_proposal_not_found", Assert.Single(denied.Issues).Code); foreign.AssertClean();
            }
            await using var scope = new LimitSafetyScope(run);
            var stale = await scope.Decide(id, approve, "limit-stale", version: "9");
            Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
            var staleReplay = await scope.Decide(id, approve, "limit-stale", version: "9"); Assert.True(staleReplay.IsReplay);
            if (!approve)
            {
                Assert.Equal(CommandOutcome.ValidationFailed, (await scope.Decide(id, false, "limit-empty", reason: " ")).Outcome);
                Assert.Equal(CommandOutcome.ValidationFailed, (await scope.Decide(id, false, "limit-long", reason: new string('x', 501))).Outcome);
            }
            var saved = await scope.Decide(id, approve, "limit-valid", reason: new string('x', 500));
            Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
            var different = await scope.Decide(id, approve, "limit-valid", version: "2", reason: new string('y', 500));
            Assert.Equal(CommandOutcome.IdempotencyConflict, different.Outcome); scope.AssertClean();
            // Stale terminal refusal is a second native receipt, not a second decision/audit.
            await LimitSafetyDurable(run, id, 2, approve ? 2 : 3, 2, 1);
        }
    }

    private static async Task<Guid> LimitSafetyPrepare(Run run)
    {
        var revision = await run.ScalarAsync<long>("SELECT COALESCE(MAX(AccountRevision),CONVERT(bigint,0)) FROM finance.PartyCreditLimits WHERE CompanyId=@company AND CompanyAccountId=@account", ("@company", CompanyA), ("@account", Account));
        await using var proposer = run.Open(CompanyA, Proposer);
        var result = await proposer.Limits.ProposeAsync(proposer.Context, Key("limit-safety-proposal-" + Guid.NewGuid().ToString("N")),
            new(Party, Account, PartyCreditLimitKind.Set, Currency, "1234.50", revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome); return result.Resource!.Id;
    }
    private static async Task LimitSafetyDurable(Run run, Guid id, long version, int status, int attempts, int audits)
    {
        Assert.Equal(version, await run.ScalarAsync<long>("SELECT Version FROM finance.PartyCreditLimits WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", id)));
        Assert.Equal(status, await run.ScalarAsync<int>("SELECT Status FROM finance.PartyCreditLimits WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", id)));
        Assert.Equal(attempts, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PartyCreditLimitCommands WHERE CompanyId=@company AND PartyCreditLimitId=@id AND CommandName IN (N'finance.credit-limit.approve',N'finance.credit-limit.reject')", ("@company", CompanyA), ("@id", id)));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@company AND EntityId=@id AND EventType IN (N'finance.credit-limit.approved.v1',N'finance.credit-limit.rejected.v1')", ("@company", CompanyA), ("@id", id.ToString("D"))));
    }
    private static AuditEventDraft LimitSafetyDraft(Guid id) => new(CompanyA, "finance.credit-limit.approved.v1", "Finance.PartyCreditLimit", id.ToString("D"), "Approve", 1, 2,
        "User", Approver, ErpPermissions.FinanceCreditLimitsApprove, [], "{}", "{}", "SENTETIK caller draft", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK-caller", null, "SENTETIK-key");

    // Real migrated SQL Finance/store + same scoped production Audit writer/collector. Settings, Counterparty,
    // authority/delegation and fault timing are controlled synthetic seams; no actual Identity/simultaneous-race claim.
    private sealed class LimitSafetyScope : IAsyncDisposable
    {
        private readonly IServiceScope owner;
        public readonly LimitSafetyCompany Company = new(); public readonly LimitSafetyUser User = new();
        public readonly LimitSafetyPermissions Permissions = new(); public FinanceDbContext Database { get; }
        public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        private readonly IFinancePartyCreditLimitMaster service; private readonly Guid company;
        public string OriginalCorrelation { get; } = "limit-safety-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; set; }
        public LimitSafetyScope(Run run, bool missingWriter = false, LimitSafetySaveProbe? probe = null, IInterceptor? extra = null, Guid? company = null)
        {
            this.company = company ?? CompanyA; Company.Set(this.company); Correlation = OriginalCorrelation;
            owner = run.Factory.Services.CreateScope(); owner.ServiceProvider.GetRequiredService<ICompanyContext>().Set(this.company);
            Collector = owner.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = owner.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (probe is not null) options.AddInterceptors(probe); if (extra is not null) options.AddInterceptors(extra);
            Database = new(options.Options, Company);
            var core = new FinancePartyCreditLimitService(Database, Company, User, Permissions, new SyntheticCandidates(), new SyntheticParty(), Collector,
                new(new(ErpPermissions.FinanceCreditLimitsPropose, [], "Web"), new(ErpPermissions.FinanceCreditLimitsApprove, [], "Web")), TimeProvider.System);
            service = new GuardedFinancePartyCreditLimitService(core, missingWriter ? null : Writer);
        }
        public Task<PartyCreditLimitCommandResult> Decide(Guid id, bool approve, string key, string version = "1", string reason = Reason, string actor = Approver) => approve
            ? service.ApproveAsync(new(company, actor, Correlation, null), new(key), new(id, version), default)
            : service.RejectAsync(new(company, actor, Correlation, null), new(key), new(id, version, reason), default);
        public void AssertClean() { Assert.DoesNotContain(Database.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted); Assert.False(Writer.HasPendingDrafts); Assert.Null(Database.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Database.DisposeAsync(); } finally { owner.Dispose(); } }
    }
    private sealed class LimitSafetyCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class LimitSafetyUser : ICurrentUser
    { public string Actor = Approver; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class LimitSafetyPermissions : IFinancePermissionPort
    {
        public Func<PermissionDecision>? BeforeRead; public Func<Task>? AfterRead;
        public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken token)
        { Assert.Equal(FinanceAction.ApproveCreditLimit, a); var answer = BeforeRead?.Invoke() ?? PermissionDecision.Allowed; if (AfterRead is not null) await AfterRead(); return answer; }
    }
    private sealed class LimitSafetySaveProbe(Guid id) : SaveChangesInterceptor
    {
        public bool Saved, FailSave; public int Attempts; public Action? BeforeFailure, AfterSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is FinanceDbContext db && db.ChangeTracker.Entries<PartyCreditLimitCommandRow>().Any(row => row.Entity.PartyCreditLimitId == id && row.State == EntityState.Added))
            { Attempts++; if (FailSave) { BeforeFailure?.Invoke(); throw new DbUpdateConcurrencyException("SENTETIK limit save fault"); } }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) { Saved = true; AfterSave?.Invoke(); } return ValueTask.FromResult(result); }
    }
    private sealed class LimitSafetyRollbackFault : DbTransactionInterceptor
    { public int Attempts; public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
      { Attempts++; throw new InvalidOperationException("SENTETIK limit rollback fault"); } }
    private sealed class LimitSafetyCommitted : DbTransactionInterceptor
    { public int Commits; public Action? AfterCommit;
      public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default)
      { Commits++; var callback = AfterCommit; AfterCommit = null; callback?.Invoke(); return Task.CompletedTask; } }
}
