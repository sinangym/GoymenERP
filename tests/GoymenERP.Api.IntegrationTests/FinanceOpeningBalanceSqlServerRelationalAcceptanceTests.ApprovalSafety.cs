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

public sealed partial class FinanceOpeningBalanceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Opening_four_decisions_late_authority_actor_company_membership_and_delegation_loss_roll_back_then_retry_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        foreach (var fault in new[] { "before-audit", "after-audit", "after-save", "precommit", "actor", "company", "membership", "delegation" })
        {
            var id = await OpeningSafetyPrepare(run, operation); var probe = new OpeningSafetySaveProbe(id, operation);
            await using var scope = new OpeningSafetyScope(run, probe: probe);
            var deny = true; var observed = false; var seenPending = false; var savedFences = 0;
            scope.Authority.BeforeRead = () =>
            {
                if (probe.Saved) savedFences++;
                if (scope.Writer.HasPendingDrafts) seenPending = true;
                var phase = fault == "before-audit" ? scope.Writer.HasPendingDrafts
                    : fault == "after-audit" ? seenPending && !scope.Writer.HasPendingDrafts
                    // Native post-save (1), wrapper post-execute (2), final precommit (3).
                    : fault == "precommit" ? savedFences >= 3 : probe.Saved;
                if (!deny || !phase) return PermissionDecision.Allowed;
                observed = true;
                if (fault == "actor") { scope.User.Actor = "SENTETIK-changed"; return PermissionDecision.Allowed; }
                if (fault == "company") { scope.Company.Set(CompanyB); return PermissionDecision.Allowed; }
                if (fault == "membership") { scope.User.Member = false; return PermissionDecision.Allowed; }
                // Controlled authority denial includes the Work delegation callback boundary, not actual WP3 acceptance.
                return PermissionDecision.Denied;
            };
            var key = "opening-late-" + id + fault;
            Assert.Equal(CommandOutcome.Denied, (await scope.Decide(id, operation, key)).Outcome);
            Assert.True(observed); if (fault == "after-audit") { Assert.True(seenPending); Assert.False(probe.Saved); }
            if (fault == "precommit") { Assert.True(probe.Saved); Assert.Equal(3, savedFences); }
            scope.AssertClean(); await OpeningSafetyDurable(run, id, operation, decided: false);
            deny = false; scope.User.Actor = Manager; scope.User.Member = true; scope.Company.Set(CompanyA); probe.Saved = false;
            Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, operation, key)).Outcome);
            await OpeningSafetyDurable(run, id, operation, decided: true);
        }
    }

    [SqlServerFact]
    public async Task Opening_decision_entry_preserves_borrowed_ambient_dirty_pending_and_first_await_caller_work()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        foreach (var boundary in new[] { "borrowed", "ambient", "dirty", "audit", "await-dirty", "await-audit", "missing-writer" })
        {
            var id = await OpeningSafetyPrepare(run, operation);
            await using var scope = new OpeningSafetyScope(run, missingWriter: boundary == "missing-writer");
            var row = new OpeningBalanceRow { Id = Guid.NewGuid(), CompanyId = CompanyA };
            if (boundary == "dirty") scope.Database.OpeningBalances.Add(row);
            if (boundary == "audit") scope.Collector.Enqueue(OpeningSafetyDraft(id));
            if (boundary.StartsWith("await-", StringComparison.Ordinal)) scope.Authority.BeforeRead = () =>
            { if (boundary == "await-dirty") scope.Database.OpeningBalances.Add(row); else scope.Collector.Enqueue(OpeningSafetyDraft(id)); return PermissionDecision.Allowed; };
            await using var borrowed = boundary == "borrowed" ? await scope.Database.Database.BeginTransactionAsync() : null;
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            var original = Transaction.Current;
            Assert.Equal(CommandOutcome.Unavailable, (await scope.Decide(id, operation, "opening-entry-" + id)).Outcome);
            Assert.Same(original, Transaction.Current); Assert.Same(borrowed, scope.Database.Database.CurrentTransaction);
            Assert.Equal(boundary is "dirty" or "await-dirty", scope.Database.ChangeTracker.HasChanges());
            Assert.Equal(boundary is "audit" or "await-audit", scope.Writer.HasPendingDrafts);
            if (boundary is "dirty" or "await-dirty") Assert.Same(row, Assert.Single(scope.Database.ChangeTracker.Entries()).Entity);
            if (ambient is null) await OpeningSafetyDurable(run, id, operation, decided: false);
            scope.Writer.DiscardPending();
        }
    }

    [SqlServerFact]
    public async Task Opening_failed_save_aborts_own_audit_before_one_readonly_original_key_pass_without_mutation_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        foreach (var publishWinner in new[] { false, true })
        {
            var id = await OpeningSafetyPrepare(run, operation); var probe = new OpeningSafetySaveProbe(id, operation) { FailSave = true };
            await using var scope = new OpeningSafetyScope(run, probe: probe);
            var key = "opening-race-" + id; var boundaries = new HashSet<Guid>(); var reconciled = false; OpeningBalanceCommandResult? winner = null;
            probe.BeforeFailure = () => scope.Collector.Enqueue(OpeningSafetyDraft(id));
            scope.Authority.AfterRead = async () =>
            {
                if (scope.Database.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId);
                if (probe.Attempts == 0 || reconciled || scope.Database.Database.CurrentTransaction is not null) return;
                reconciled = true; scope.AssertClean(); Assert.Equal(1, probe.Attempts);
                if (!publishWinner) return;
                await using var actual = new OpeningSafetyScope(run); winner = await actual.Decide(id, operation, key);
                Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); Assert.False(winner.IsReplay);
            };
            var result = await scope.Decide(id, operation, key);
            Assert.True(reconciled); Assert.Equal(2, boundaries.Count); Assert.Equal(1, probe.Attempts); scope.AssertClean();
            if (publishWinner)
            {
                Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId);
                Assert.Equal(JsonSerializer.Serialize(winner.Resource, FinanceJson.CreateOptions()), JsonSerializer.Serialize(result.Resource, FinanceJson.CreateOptions()));
            }
            else { Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code); }
            await OpeningSafetyDurable(run, id, operation, decided: publishWinner);
            if (!publishWinner)
            {
                probe.FailSave = false; Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, operation, key)).Outcome);
                Assert.Equal(2, probe.Attempts); await OpeningSafetyDurable(run, id, operation, decided: true);
            }
        }
    }

    [SqlServerFact]
    public async Task Opening_rollback_or_dispose_fault_still_cleans_owned_work_and_never_reads_a_winner()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        foreach (var dispose in new[] { false, true })
        {
            var id = await OpeningSafetyPrepare(run, operation); var probe = new OpeningSafetySaveProbe(id, operation) { FailSave = true };
            var rollback = new OpeningSafetyRollbackFault(); var disposal = new OpeningSafetyDisposeFault();
            await using var scope = new OpeningSafetyScope(run, probe: probe, extra: dispose ? null : rollback, disposal: dispose ? disposal : null);
            var boundaries = new HashSet<Guid>(); probe.BeforeFailure = () => scope.Collector.Enqueue(OpeningSafetyDraft(id));
            scope.Authority.AfterRead = () => { if (scope.Database.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId); return Task.CompletedTask; };
            var error = await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(id, operation, "opening-cleanup-" + id));
            Assert.Contains(error.InnerExceptions, item => item is InvalidOperationException && item.Message == (dispose ? "SENTETIK opening dispose fault" : "SENTETIK opening rollback fault"));
            Assert.Equal(1, dispose ? disposal.Attempts : rollback.Attempts); Assert.Single(boundaries); Assert.Equal(1, probe.Attempts);
            scope.AssertClean(); await OpeningSafetyDurable(run, id, operation, decided: false);
        }
    }

    [SqlServerFact]
    public async Task Opening_save_created_audit_is_rolled_back_and_does_not_poison_same_key_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        {
            var id = await OpeningSafetyPrepare(run, operation); var probe = new OpeningSafetySaveProbe(id, operation);
            await using var scope = new OpeningSafetyScope(run, probe: probe); probe.AfterSave = () => scope.Collector.Enqueue(OpeningSafetyDraft(id));
            var key = "opening-save-audit-" + id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Decide(id, operation, key));
            scope.AssertClean(); await OpeningSafetyDurable(run, id, operation, decided: false);
            probe.AfterSave = null; Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, operation, key)).Outcome);
            await OpeningSafetyDurable(run, id, operation, decided: true);
        }
    }

    [SqlServerFact]
    public async Task Opening_committed_decision_release_refusal_keeps_single_movement_attempt_audit_and_original_replay()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        {
            var id = await OpeningSafetyPrepare(run, operation); var committed = new OpeningSafetyCommitted();
            await using var scope = new OpeningSafetyScope(run, extra: committed); committed.AfterCommit = () => scope.User.Member = false;
            var key = "opening-committed-" + id;
            Assert.Equal(CommandOutcome.Denied, (await scope.Decide(id, operation, key)).Outcome);
            Assert.Equal(1, committed.Commits); scope.AssertClean(); await OpeningSafetyDurable(run, id, operation, decided: true);
            var original = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.OpeningBalanceCommands WHERE CompanyId=@company AND OpeningBalanceId=@id AND CommandName=@command",
                ("@company", CompanyA), ("@id", id), ("@command", OpeningSafetyCommand(operation)));
            committed.AfterCommit = null; scope.User.Member = true; scope.Correlation = "SENTETIK-new-correlation";
            // Later reversal metadata must not be remapped into the first immutable command result.
            if (operation == 0) { await OpeningSafetyProposeReversal(run, id); await using var later = new OpeningSafetyScope(run); Assert.Equal(CommandOutcome.Succeeded, (await later.Decide(id, 2, "opening-later-" + id)).Outcome); }
            if (operation == 3) await OpeningSafetyProposeReversal(run, id);
            var replay = await scope.Decide(id, operation, key);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay); Assert.Equal(scope.OriginalCorrelation, replay.OriginalCorrelationId);
            var stored = JsonSerializer.Deserialize<OpeningBalanceCommandResult>(original, FinanceJson.CreateOptions())!;
            Assert.Equal(JsonSerializer.Serialize(stored.Resource, FinanceJson.CreateOptions()), JsonSerializer.Serialize(replay.Resource, FinanceJson.CreateOptions()));
            Assert.Equal(original, await run.ScalarAsync<string>("SELECT ResultJson FROM finance.OpeningBalanceCommands WHERE CompanyId=@company AND OpeningBalanceId=@id AND CommandName=@command",
                ("@company", CompanyA), ("@id", id), ("@command", OpeningSafetyCommand(operation))));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceCommands WHERE CompanyId=@company AND OpeningBalanceId=@id AND CommandName=@command",
                ("@company", CompanyA), ("@id", id), ("@command", OpeningSafetyCommand(operation))));
            scope.AssertClean();
        }
    }

    [SqlServerFact]
    public async Task Opening_native_maker_company_version_reason_period_and_original_receipt_rules_survive_owned_decisions()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        {
            var id = await OpeningSafetyPrepare(run, operation);
            await using (var maker = new OpeningSafetyScope(run))
            { maker.User.Actor = Specialist; Assert.Equal("finance_approver_is_proposer", Assert.Single((await maker.Decide(id, operation, "opening-maker-" + id, actor: Specialist)).Issues).Code); maker.AssertClean(); }
            await using (var foreign = new OpeningSafetyScope(run, company: CompanyB))
            { Assert.Null((await foreign.Decide(id, operation, "opening-foreign-" + id)).Resource); foreign.AssertClean(); }
            await using var scope = new OpeningSafetyScope(run);
            var stale = await scope.Decide(id, operation, "opening-stale-" + id, version: "9"); Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
            var replay = await scope.Decide(id, operation, "opening-stale-" + id, version: "9"); Assert.True(replay.IsReplay); Assert.Equal(stale.OriginalCorrelationId, replay.OriginalCorrelationId);
            if (operation is 1 or 3)
                foreach (var reason in new[] { " ", new string('x', 501) }) Assert.Equal(CommandOutcome.ValidationFailed, (await scope.Decide(id, operation, "opening-reason-" + id, reason: reason)).Outcome);
            scope.Period.Answer = new(OpeningPeriodState.Closed, PeriodVersion);
            Assert.Equal("opening_period_closed", Assert.Single((await scope.Decide(id, operation, "opening-period-" + id)).Issues).Code);
            scope.Period.Answer = new(OpeningPeriodState.Open, "SENTETIK-changed-period");
            Assert.Equal("opening_period_version_changed", Assert.Single((await scope.Decide(id, operation, "opening-period-changed-" + id)).Issues).Code);
            scope.Period.Answer = new(OpeningPeriodState.Open, PeriodVersion);
            Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(id, operation, "opening-valid-" + id, reason: new string('x', 500))).Outcome);
            Assert.Equal(CommandOutcome.IdempotencyConflict, (await scope.Decide(id, operation, "opening-valid-" + id, version: "2")).Outcome);
            scope.AssertClean(); await OpeningSafetyDurable(run, id, operation, decided: true, attempts: 2);
        }
    }

    [SqlServerFact]
    public async Task Opening_induced_direct_wrapped_and_provider_aborted_1205_read_one_original_key_without_mutation_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        foreach (var wrapped in new[] { false, true })
        foreach (var completedProvider in new[] { false, true })
        foreach (var publishWinner in new[] { false, true })
        {
            var id = await OpeningSafetyPrepare(run, operation); var probe = new OpeningSafetySaveProbe(id, operation);
            await using var scope = new OpeningSafetyScope(run, probe: probe);
            var injected = false; var seenPending = false; var reconciled = false; var boundaries = new HashSet<Guid>();
            OpeningBalanceCommandResult? winner = null; var key = "opening-induced-1205-" + id;
            scope.Authority.BeforeRead = () => { if (scope.Writer.HasPendingDrafts) seenPending = true; return PermissionDecision.Allowed; };
            scope.Authority.AfterRead = async () =>
            {
                var transaction = scope.Database.Database.CurrentTransaction;
                if (transaction is not null) boundaries.Add(transaction.TransactionId);
                if (!injected && seenPending && !scope.Writer.HasPendingDrafts && transaction is not null)
                {
                    // Existing provider-error primitive, not a real simultaneous-deadlock reproduction.
                    if (completedProvider) { var sql = Assert.IsType<SqlTransaction>(transaction.GetDbTransaction()); await sql.RollbackAsync(); Assert.Null(sql.Connection); }
                    injected = true; var victim = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (wrapped) throw new DbUpdateException("SENTETIK induced opening victim", victim);
                    throw victim;
                }
                if (!injected || reconciled || transaction is not null) return;
                reconciled = true; scope.AssertClean();
                if (publishWinner) { await using var actual = new OpeningSafetyScope(run); winner = await actual.Decide(id, operation, key); Assert.Equal(CommandOutcome.Succeeded, winner.Outcome); }
            };
            var result = await scope.Decide(id, operation, key);
            Assert.True(injected); Assert.True(reconciled); Assert.Equal(2, boundaries.Count); Assert.Equal(0, probe.Attempts); scope.AssertClean();
            if (publishWinner) { Assert.NotNull(winner); Assert.True(result.IsReplay); Assert.Equal(winner.OriginalCorrelationId, result.OriginalCorrelationId); }
            else { Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.False(result.IsReplay); Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code); }
            await OpeningSafetyDurable(run, id, operation, decided: publishWinner);
        }
    }

    [SqlServerFact]
    public async Task Opening_induced_1205_in_readonly_recovery_propagates_without_a_third_pass_or_new_movement()
    {
        await using var run = await Run.StartAsync();
        foreach (var operation in Enumerable.Range(0, 4))
        {
            var id = await OpeningSafetyPrepare(run, operation); var probe = new OpeningSafetySaveProbe(id, operation);
            await using var scope = new OpeningSafetyScope(run, probe: probe);
            var boundaries = new HashSet<Guid>();
            scope.Authority.BeforeRead = () =>
            {
                if (scope.Database.Database.CurrentTransaction is { } tx && boundaries.Add(tx.TransactionId))
                    throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                return PermissionDecision.Allowed;
            };
            var error = await Assert.ThrowsAsync<SqlException>(() => scope.Decide(id, operation, "opening-second-victim-" + id));
            Assert.Equal(1205, error.Number); Assert.Equal(2, boundaries.Count); Assert.Equal(0, probe.Attempts);
            scope.AssertClean(); await OpeningSafetyDurable(run, id, operation, decided: false);
        }
    }

    private static string OpeningSafetyCommand(int operation) => operation switch
    { 0 => FinanceOpeningBalanceService.ApproveCommand, 1 => FinanceOpeningBalanceService.RejectCommand, 2 => FinanceOpeningBalanceService.ApproveReversalCommand, _ => FinanceOpeningBalanceService.RejectReversalCommand };

    private static async Task<Guid> OpeningSafetyPrepare(Run run, int operation)
    {
        await using var maker = new OpeningSafetyScope(run); maker.User.Actor = Specialist;
        var request = Request("001250.50") with { TargetId = Guid.NewGuid() };
        var result = await maker.Service.ProposeAsync(new(CompanyA, Specialist, "SENTETIK-propose", null), new("opening-prepare-" + Guid.NewGuid()), request, default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome); var id = result.Resource!.Id;
        if (operation >= 2)
        {
            await using var checker = new OpeningSafetyScope(run);
            Assert.Equal(CommandOutcome.Succeeded, (await checker.Decide(id, 0, "opening-base-" + id)).Outcome);
            await OpeningSafetyProposeReversal(run, id);
        }
        return id;
    }
    private static async Task OpeningSafetyProposeReversal(Run run, Guid id)
    {
        await using var maker = new OpeningSafetyScope(run); maker.User.Actor = Specialist;
        Assert.Equal(CommandOutcome.Succeeded, (await maker.Service.ProposeReversalAsync(new(CompanyA, Specialist, "SENTETIK-reversal", null),
            new("opening-reversal-" + Guid.NewGuid()), new(id, "2", "SENTETIK düzeltme"), default)).Outcome);
    }
    private static async Task OpeningSafetyDurable(Run run, Guid id, int operation, bool decided, int? attempts = null)
    {
        var reversal = operation >= 2; var approve = operation is 0 or 2;
        Assert.Equal(reversal ? decided && approve ? 3L : 2L : decided ? 2L : 1L,
            await run.ScalarAsync<long>("SELECT Version FROM finance.OpeningBalances WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", id)));
        Assert.Equal(reversal ? decided && approve ? 4 : 2 : decided ? approve ? 2 : 3 : 1,
            await run.ScalarAsync<int>("SELECT Status FROM finance.OpeningBalances WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", id)));
        if (reversal)
        {
            Assert.Equal(decided ? 2L : 1L, await run.ScalarAsync<long>("SELECT Version FROM finance.OpeningBalanceReversals WHERE CompanyId=@company AND OpeningBalanceId=@id", ("@company", CompanyA), ("@id", id)));
            Assert.Equal(decided ? approve ? 2 : 3 : 1, await run.ScalarAsync<int>("SELECT Status FROM finance.OpeningBalanceReversals WHERE CompanyId=@company AND OpeningBalanceId=@id", ("@company", CompanyA), ("@id", id)));
        }
        Assert.Equal((reversal ? 1 : 0) + (decided && approve ? 1 : 0), await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceMovements WHERE CompanyId=@company AND OpeningBalanceId=@id", ("@company", CompanyA), ("@id", id)));
        Assert.Equal(attempts ?? (decided ? 1 : 0), await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceCommands WHERE CompanyId=@company AND OpeningBalanceId=@id AND CommandName=@command",
            ("@company", CompanyA), ("@id", id), ("@command", OpeningSafetyCommand(operation))));
        var eventType = operation switch { 0 => "finance.opening-balance.approved.v1", 1 => "finance.opening-balance.rejected.v1", 2 => "finance.opening-balance.reversal-approved.v1", _ => "finance.opening-balance.reversal-rejected.v1" };
        Assert.Equal(decided ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@company AND EntityId=@id AND EventType=@event",
            ("@company", CompanyA), ("@id", id.ToString("D")), ("@event", eventType)));
    }
    private static AuditEventDraft OpeningSafetyDraft(Guid id) => new(CompanyA, "finance.opening-balance.approved.v1", "Finance.OpeningBalance", id.ToString("D"), "Approve", 1, 2,
        "User", Manager, ErpPermissions.FinanceOpeningBalancesApprove, [], "{}", "{}", "SENTETIK own fault draft", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK", null, "SENTETIK-key");

    // Real migrated Finance/Audit storage and scoped actual writer. Settings/party/authority are controlled synthetic ports.
    // Unique synthetic target IDs are admitted through the native proposal, never patched into an existing money fact.
    private sealed class OpeningSafetyScope : IAsyncDisposable
    {
        private readonly IServiceScope owner; private readonly Guid company;
        public readonly OpeningSafetyCompany Company = new(); public readonly OpeningSafetyUser User = new(); public readonly OpeningSafetyAuthority Authority = new(); public readonly OpeningSafetyPeriod Period = new();
        public FinanceDbContext Database { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; } public IFinanceOpeningBalanceService Service { get; }
        public string OriginalCorrelation { get; } = "opening-safety-" + Guid.NewGuid().ToString("N"); public string Correlation { get; set; }
        public OpeningSafetyScope(Run run, OpeningSafetySaveProbe? probe = null, IInterceptor? extra = null, bool missingWriter = false, Guid? company = null, OpeningSafetyDisposeFault? disposal = null)
        {
            this.company = company ?? CompanyA; Company.Set(this.company); Correlation = OriginalCorrelation;
            owner = run.Factory.Services.CreateScope(); owner.ServiceProvider.GetRequiredService<ICompanyContext>().Set(this.company);
            Collector = owner.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = owner.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company)); if (probe is not null) options.AddInterceptors(probe); if (extra is not null) options.AddInterceptors(extra);
            if (disposal is not null) options.LogTo((id, _) => id == RelationalEventId.TransactionDisposed, _ => disposal.OnDispose());
            Database = new(options.Options, Company);
            var envelopes = new FinanceOpeningBalanceEnvelopes(new(ErpPermissions.FinanceOpeningBalancesPropose, [], "Web"), new(ErpPermissions.FinanceOpeningBalancesApprove, [], "Web"));
            Service = new FinanceOpeningBalanceService(Database, Company, User, Authority, new SyntheticCandidates(), new OpeningSafetyParty(), Period, Collector,
                envelopes, TimeProvider.System, new InternalLedgerTreasuryBalanceGuard(Database, Company), missingWriter ? null : Writer).WithApprovalSafety();
        }
        public Task<OpeningBalanceCommandResult> Decide(Guid id, int operation, string key, string version = "1", string reason = "SENTETIK ret", string actor = Manager)
        {
            var c = new FinanceContext(company, actor, Correlation, null); var m = new CommandMetadata(key);
            return operation switch { 0 => Service.ApproveAsync(c, m, new(id, version), default), 1 => Service.RejectAsync(c, m, new(id, version, reason), default),
                2 => Service.ApproveReversalAsync(c, m, new(id, version), default), _ => Service.RejectReversalAsync(c, m, new(id, version, reason), default) };
        }
        public void AssertClean() { Assert.DoesNotContain(Database.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted); Assert.False(Writer.HasPendingDrafts); Assert.Null(Database.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Database.DisposeAsync(); } finally { owner.Dispose(); } }
    }
    private sealed class OpeningSafetyCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class OpeningSafetyUser : ICurrentUser
    { public string Actor = Manager; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, CompanyB }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class OpeningSafetyAuthority : IFinancePermissionPort
    {
        public Func<PermissionDecision>? BeforeRead; public Func<Task>? AfterRead;
        public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
        { Assert.Contains(a, new[] { FinanceAction.ApproveOpeningBalance, FinanceAction.ProposeOpeningBalance }); var answer = BeforeRead?.Invoke() ?? PermissionDecision.Allowed; if (AfterRead is not null) await AfterRead(); return answer; }
    }
    private sealed class OpeningSafetyPeriod : IFinanceOpeningPeriodReader
    { public FinanceOpeningPeriodAnswer Answer = new(OpeningPeriodState.Open, PeriodVersion); public Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid company, CancellationToken t) => Task.FromResult(Answer); }
    private sealed class OpeningSafetyParty : IFinancePartyAccountPort
    { public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext c, Guid party, Guid account, CancellationToken t) => Task.FromResult(new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, new(party, account, c.CompanyId, true, IsPartyActive: true))); }
    private sealed class OpeningSafetySaveProbe(Guid id, int operation) : SaveChangesInterceptor
    {
        public bool Saved, FailSave; public int Attempts; public Action? BeforeFailure, AfterSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { if (data.Context is FinanceDbContext db && db.ChangeTracker.Entries<OpeningBalanceCommandRow>().Any(row => row.State == EntityState.Added && row.Entity.OpeningBalanceId == id && row.Entity.CommandName == OpeningSafetyCommand(operation))) { Attempts++; if (FailSave) { BeforeFailure?.Invoke(); throw new DbUpdateConcurrencyException("SENTETIK opening save fault"); } } return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) { Saved = true; AfterSave?.Invoke(); } return ValueTask.FromResult(result); }
    }
    private sealed class OpeningSafetyRollbackFault : DbTransactionInterceptor
    { public int Attempts; public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default) { Attempts++; throw new InvalidOperationException("SENTETIK opening rollback fault"); } }
    private sealed class OpeningSafetyDisposeFault
    { public int Attempts; public void OnDispose() { if (++Attempts == 1) throw new InvalidOperationException("SENTETIK opening dispose fault"); } }
    private sealed class OpeningSafetyCommitted : DbTransactionInterceptor
    { public int Commits; public Action? AfterCommit; public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default) { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; } }
}
