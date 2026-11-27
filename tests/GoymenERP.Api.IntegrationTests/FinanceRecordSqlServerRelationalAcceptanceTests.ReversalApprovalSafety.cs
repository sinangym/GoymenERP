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

public sealed partial class FinanceRecordSqlServerRelationalAcceptanceTests
{
    private const string ReceiptChecker = "SENTETIK-receipt-reversal-checker";
    // Existing FIN-T06 fixture records the original receipt and proposal through native commands. Bank/party/profile
    // inputs and permission timing remain synthetic; decisions use migrated Finance SQL and the real scoped Audit writer.
    [SqlServerFact]
    public async Task Native_receipt_reversal_late_fences_roll_back_counter_event_barrier_receipt_and_audit_then_same_key_executes_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "before-audit", "after-audit", "after-save", "precommit", "actor", "company", "membership", "delegation" })
        {
            var (source, proposal) = await ReceiptDecisionSafetyPrepare(run); var save = new ReceiptDecisionSafetySave(proposal);
            await using var scope = new ReceiptDecisionSafetyScope(run, save: save);
            var deny = true; var seenPending = false; var observed = false; var savedFences = 0;
            scope.Permissions.Check = () =>
            {
                var staged = scope.Database.ChangeTracker.Entries<ReversalProposalRow>().Any(e => e.Entity.Id == proposal && e.Entity.Version == 2);
                if (staged && scope.Writer.HasPendingDrafts) seenPending = true;
                if (save.Saved) savedFences++;
                // Save post-fence (1), receipt read before/after Commands/after original facts (2/3/4), wrapper post-execution (5), precommit (6).
                var reached = phase == "before-audit" ? staged && scope.Writer.HasPendingDrafts
                    : phase == "after-audit" ? staged && seenPending && !scope.Writer.HasPendingDrafts
                    : phase == "precommit" ? savedFences >= 6 : save.Saved;
                if (!deny || !reached) return PermissionDecision.Allowed;
                observed = true;
                if (phase == "actor") { scope.User.Actor = "SENTETIK-other"; return PermissionDecision.Allowed; }
                if (phase == "company") { scope.Company.Set(TestCompanyIds.CompanyB); return PermissionDecision.Allowed; }
                if (phase == "membership") { scope.User.Member = false; return PermissionDecision.Allowed; }
                return PermissionDecision.Denied;
            };
            var refused = await scope.Decide(proposal, approve, "reversal-late-" + proposal);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome); Assert.True(observed);
            if (phase == "precommit") Assert.Equal(6, savedFences);
            if (phase == "after-audit") { Assert.True(seenPending); Assert.False(save.Saved); }
            scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
            deny = false; scope.User.Actor = ReceiptChecker; scope.User.Member = true; scope.Company.Set(CompanyA); save.Saved = false;
            Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(proposal, approve, "reversal-late-" + proposal)).Outcome);
            scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Native_receipt_reversal_entry_preserves_borrowed_ambient_dirty_pending_and_await_created_caller_state()
    {
        await using var run = await Run.StartAsync(); var (source, proposal) = await ReceiptDecisionSafetyPrepare(run);
        foreach (var boundary in new[] { "missing-writer", "borrowed", "ambient", "dirty", "audit", "await-dirty", "await-audit" })
        {
            await using var scope = new ReceiptDecisionSafetyScope(run, missingWriter: boundary == "missing-writer");
            await using var borrowed = boundary == "borrowed" ? await scope.Database.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
            using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            void AddCaller()
            {
                if (boundary is "dirty" or "await-dirty") scope.Database.Commands.Add(new() { CompanyId = CompanyA, CommandName = "SENTETIK-caller" });
                else scope.Collector.Enqueue(ReceiptDecisionSafetyDraft(proposal));
            }
            if (boundary is "dirty" or "audit") AddCaller();
            if (boundary is "await-dirty" or "await-audit") scope.Permissions.AfterRead = () => { scope.Permissions.AfterRead = null; AddCaller(); return Task.CompletedTask; };
            var result = await scope.Decide(proposal, true, "reversal-entry-" + boundary);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
            Assert.Equal(boundary is "dirty" or "await-dirty", scope.Database.ChangeTracker.HasChanges());
            Assert.Equal(boundary is "audit" or "await-audit", scope.Writer.HasPendingDrafts);
            if (borrowed is not null) Assert.Same(borrowed, scope.Database.Database.CurrentTransaction);
        }
        await ReceiptDecisionSafetyDurable(run, source, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
    }

    [SqlServerFact]
    public async Task Native_receipt_reversal_failed_save_or_controlled_1205_aborts_before_one_readonly_original_key_pass_without_mutation_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "concurrency", "direct-audit-1205", "wrapped-save-1205", "provider-aborted-1205" })
        foreach (var publishWinner in new[] { false, true })
        {
            var (source, proposal) = await ReceiptDecisionSafetyPrepare(run);
            var save = new ReceiptDecisionSafetySave(proposal) { FailSave = phase is "concurrency" or "wrapped-save-1205", Deadlock = phase == "wrapped-save-1205" };
            await using var scope = new ReceiptDecisionSafetyScope(run, save: save);
            var injected = false; var reconciled = false; var boundaries = new HashSet<Guid>(); var key = "reversal-race-" + proposal;
            ReversalProposalCommandResult? winner = null;
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
                await using var other = new ReceiptDecisionSafetyScope(run); winner = await other.Decide(proposal, approve, key);
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
                await ReceiptDecisionSafetyDurable(run, source, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
            }
            else
            {
                Assert.Equal(CommandOutcome.VersionConflict, result.Outcome); Assert.False(result.IsReplay); Assert.Equal("concurrent_change", Assert.Single(result.Issues).Code);
                await ReceiptDecisionSafetyDurable(run, source, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
                save.FailSave = false;
                Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(proposal, approve, key)).Outcome);
                await ReceiptDecisionSafetyDurable(run, source, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
            }
        }
    }

    [SqlServerFact]
    public async Task Native_receipt_reversal_cleanup_faults_clear_owned_work_and_never_start_a_winner_pass()
    {
        await using var run = await Run.StartAsync(); var (source, proposal) = await ReceiptDecisionSafetyPrepare(run);
        var rollback = new ReceiptDecisionSafetyRollbackFault(); await using var scope = new ReceiptDecisionSafetyScope(run, extra: rollback);
        var boundaries = new HashSet<Guid>(); scope.Permissions.Check = () =>
        {
            if (scope.Database.Database.CurrentTransaction is { } tx) boundaries.Add(tx.TransactionId);
            if (scope.Writer.HasPendingDrafts) throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
            return PermissionDecision.Allowed;
        };
        var error = await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(proposal, true, "reversal-cleanup"));
        Assert.Contains(error.InnerExceptions, item => item is SqlException { Number: 1205 }); Assert.Equal(1, rollback.Attempts); Assert.Single(boundaries);
        scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
    }

    [SqlServerFact]
    public async Task Native_receipt_reversal_postcommit_release_refusal_keeps_original_receipt_and_replay_never_duplicates_money()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var (source, proposal) = await ReceiptDecisionSafetyPrepare(run); var committed = new ReceiptDecisionSafetyCommitted();
            await using var scope = new ReceiptDecisionSafetyScope(run, extra: committed);
            committed.AfterCommit = () => scope.User.Member = false; var key = "reversal-committed-" + proposal;
            Assert.Equal(CommandOutcome.Denied, (await scope.Decide(proposal, approve, key)).Outcome); Assert.Equal(1, committed.Commits);
            scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
            committed.AfterCommit = null; scope.User.Member = true; scope.Correlation = "SENTETIK-retry";
            var result = await scope.Decide(proposal, approve, key); Assert.True(result.IsReplay); Assert.Equal(scope.OriginalCorrelation, result.OriginalCorrelationId);
            using var wire = JsonDocument.Parse(JsonSerializer.Serialize(result, FinanceJson.CreateOptions())); Assert.Equal(5, wire.RootElement.EnumerateObject().Count());
            Assert.Equal("2", result.Resource!.Version);
            scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
        }
    }

    [SqlServerFact]
    public async Task Native_receipt_reversal_save_created_audit_ghost_is_discarded_and_terminal_version_conflict_receipt_replays_unchanged()
    {
        await using var run = await Run.StartAsync(); var (source, proposal) = await ReceiptDecisionSafetyPrepare(run);
        var save = new ReceiptDecisionSafetySave(proposal); await using var scope = new ReceiptDecisionSafetyScope(run, save: save);
        save.AfterSave = () => scope.Collector.Enqueue(ReceiptDecisionSafetyDraft(proposal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Decide(proposal, true, "reversal-save-ghost"));
        scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
        save.AfterSave = null;
        var conflict = await scope.Decide(proposal, true, "reversal-terminal-conflict", version: "9"); Assert.Equal(CommandOutcome.VersionConflict, conflict.Outcome);
        var replay = await scope.Decide(proposal, true, "reversal-terminal-conflict", version: "9"); Assert.True(replay.IsReplay); Assert.Equal(conflict.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(CommandOutcome.Succeeded, (await scope.Decide(proposal, true, "reversal-save-ghost")).Outcome);
        scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 2, ReversalProposalStatuses.Approved, 2, 1, 1);
    }

    [SqlServerFact]
    public async Task Native_receipt_reversal_second_readonly_fault_and_postcommit_1205_never_retry_mutation()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "readonly-second", "postcommit" })
        {
            var (source, proposal) = await ReceiptDecisionSafetyPrepare(run); var save = new ReceiptDecisionSafetySave(proposal) { FailSave = phase == "readonly-second" };
            var committed = new ReceiptDecisionSafetyCommitted(); await using var scope = new ReceiptDecisionSafetyScope(run, save: save, extra: committed);
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
            if (phase == "readonly-second") await ReceiptDecisionSafetyDurable(run, source, proposal, 1, ReversalProposalStatuses.Pending, 0, 0, 0);
            else
            {
                await ReceiptDecisionSafetyDurable(run, source, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
                scope.Permissions.Check = null;
                var replay = await scope.Decide(proposal, approve, key); Assert.True(replay.IsReplay); Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
                Assert.Equal(1, save.Attempts); scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 2, approve ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Rejected, 1, 1, approve ? 1 : 0);
            }
        }
    }

    [SqlServerFact]
    public async Task Two_real_guarded_receipt_reversal_sessions_at_the_pre_audit_boundary_commit_one_complete_economic_outcome()
    {
        await using var run = await Run.StartAsync(); var (source, proposal) = await ReceiptDecisionSafetyPrepare(run);
        await using var first = new ReceiptDecisionSafetyScope(run); await using var second = new ReceiptDecisionSafetyScope(run);
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
        await ReceiptDecisionSafetyDurable(run, source, proposal, 2, ReversalProposalStatuses.Approved, 1, 1, 1);
    }

    [SqlServerFact]
    public async Task Native_receipt_reversal_maker_hash_reason_and_historical_replay_keep_native_decision_semantics()
    {
        await using var run = await Run.StartAsync(); var (source, proposal) = await ReceiptDecisionSafetyPrepare(run);
        await using var scope = new ReceiptDecisionSafetyScope(run); var key = "receipt-original-reject-" + proposal;
        scope.User.Actor = run.Actor;
        var makerContext = new FinanceContext(CompanyA, run.Actor, "SENTETIK-maker", null);
        Assert.Equal(CommandOutcome.Denied, (await scope.Service.ApproveAsync(makerContext, new("maker-approve"), new(proposal, "1"), default)).Outcome);
        Assert.Equal(CommandOutcome.Denied, (await scope.Service.RejectAsync(makerContext, new("maker-reject"), new(proposal, "1", "ret"), default)).Outcome);
        scope.User.Actor = ReceiptChecker; scope.AssertClean();
        Assert.Equal(CommandOutcome.ValidationFailed, (await scope.Service.RejectAsync(new(CompanyA, ReceiptChecker, "SENTETIK-limit", null),
            new("too-long"), new(proposal, "1", new string('x', 1001)), default)).Outcome);
        var first = await scope.Service.RejectAsync(new(CompanyA, ReceiptChecker, scope.OriginalCorrelation, null), new(key), new(proposal, "1", "  önceki kayıt  "), default);
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome); Assert.Equal("önceki kayıt", first.Resource!.DecisionReason);
        Assert.Equal("2", first.Resource.Version); scope.AssertClean();
        // A later proposal is a new native request. It must never replace the first key's immutable receipt.
        await using (var maker = run.ReversalService(CompanyA, run.Actor))
        {
            var later = await maker.Service.ProposeAsync(maker.Context, Key("later-" + source), new(source, "2", OccurredOn.AddDays(3), "SENTETIK sonraki teklif"), default);
            Assert.Equal(CommandOutcome.Succeeded, later.Outcome); Assert.NotEqual(proposal, later.Resource!.Id);
        }
        var replay = await scope.Service.RejectAsync(new(CompanyA, ReceiptChecker, "SENTETIK-new-correlation", null), new(key), new(proposal, "1", "  önceki kayıt  "), default);
        Assert.True(replay.IsReplay); Assert.Equal(first.Resource, replay.Resource); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        var different = await scope.Service.RejectAsync(new(CompanyA, ReceiptChecker, "SENTETIK-conflict", null), new(key), new(proposal, "1", "başka gerekçe"), default);
        Assert.Equal(CommandOutcome.IdempotencyConflict, different.Outcome); Assert.Null(different.Resource);
        scope.AssertClean(); await ReceiptDecisionSafetyDurable(run, source, proposal, 2, ReversalProposalStatuses.Rejected, 1, 1, 0);
    }

    private static async Task<(Guid Source, Guid Proposal)> ReceiptDecisionSafetyPrepare(Run run)
    {
        var owners = Owners.For(CompanyA); await run.SeedApprovedBankAccountAsync(owners);
        var draft = await run.SaveDraftAsync(owners, "1250.50");
        CommandResult recorded;
        await using (var scope = run.Service(CompanyA, owners))
            recorded = await scope.Service.RecordAsync(scope.Context, Key("safety-record-" + draft.Id), new(draft.Id, draft.Version), default);
        Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        ReversalProposalCommandResult proposed;
        await using (var scope = run.ReversalService(CompanyA, run.Actor))
            proposed = await scope.Service.ProposeAsync(scope.Context, Key("safety-propose-" + draft.Id),
                new(draft.Id, recorded.Resource!.Source.Version, OccurredOn.AddDays(2), "SENTETIK correction"), default);
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome); return (draft.Id, proposed.Resource!.Id);
    }
    private static async Task ReceiptDecisionSafetyDurable(Run run, Guid source, Guid proposal, long version, int status, int commands, int audits, int events)
    {
        Assert.Equal(version, await run.ScalarAsync<long>("SELECT Version FROM finance.ReversalProposals WHERE CompanyId=@c AND Id=@p", ("@c", CompanyA), ("@p", proposal)));
        Assert.Equal(status, await run.ScalarAsync<int>("SELECT Status FROM finance.ReversalProposals WHERE CompanyId=@c AND Id=@p", ("@c", CompanyA), ("@p", proposal)));
        Assert.Equal(commands, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands WHERE CompanyId=@c AND ReversalProposalId=@p AND CommandName IN (N'finance.receipt-reversal.approve',N'finance.receipt-reversal.reject')", ("@c", CompanyA), ("@p", proposal)));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@c AND EntityId=@p AND EventType IN (N'finance.receipt-reversal.approved.v1',N'finance.receipt-reversal.rejected.v1')", ("@c", CompanyA), ("@p", proposal.ToString("D"))));
        Assert.Equal(events, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents e JOIN finance.ReversalProposals p ON p.CompanyId=e.CompanyId AND p.ReversalSourceId=e.SourceId WHERE p.CompanyId=@c AND p.Id=@p AND e.Kind=2", ("@c", CompanyA), ("@p", proposal)));
        Assert.Equal(events, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts s JOIN finance.ReversalProposals p ON p.CompanyId=s.CompanyId AND p.ReversalSourceId=s.Id WHERE p.CompanyId=@c AND p.Id=@p AND s.Stage=1 AND s.Version=1", ("@c", CompanyA), ("@p", proposal)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts WHERE CompanyId=@c AND Id=@s AND Version=2 AND Stage=1 AND AmountText=N'1250.50'", ("@c", CompanyA), ("@s", source)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents WHERE CompanyId=@c AND SourceId=@s AND Kind=1 AND AmountText=N'1250.50'", ("@c", CompanyA), ("@s", source)));
        Assert.Equal(events, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.SourceAllocationStates WHERE CompanyId=@c AND SourceId=@s AND UnallocatedAmountText=N'0.00'", ("@c", CompanyA), ("@s", source)));
    }
    private static AuditEventDraft ReceiptDecisionSafetyDraft(Guid proposal) => new(CompanyA, "finance.receipt-reversal.approved.v1", "Finance.ReversalProposal", proposal.ToString("D"), "Approve", 1, 2,
        "User", ReceiptChecker, FinancePermissionNames.ReverseApprove, [], "{}", "{}", "SENTETIK caller draft", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK", null, "SENTETIK-key");
    private sealed class ReceiptDecisionSafetyScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public ReceiptDecisionSafetyCompany Company { get; } = new(); public ReceiptDecisionSafetyUser User { get; } = new(); public ReceiptDecisionSafetyPermissions Permissions { get; } = new();
        public FinanceDbContext Database { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public IReversalProposalService Service { get; } public string OriginalCorrelation { get; } = "reversal-safety-" + Guid.NewGuid().ToString("N"); public string Correlation { get; set; }
        public ReceiptDecisionSafetyScope(Run run, ReceiptDecisionSafetySave? save = null, IInterceptor? extra = null, bool missingWriter = false)
        {
            Correlation = OriginalCorrelation; Company.Set(CompanyA); auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company)); if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra);
            Database = new(options.Options, Company); var envelope = new FinanceAuditEnvelope(FinancePermissionNames.ReverseApprove, [], "Web");
            Service = new ReversalProposalService(Database, Company, User, Permissions, new SyntheticOpenPeriodRule(), new FinanceAllocationReversalGate(Database, Company, User),
                Collector, new(envelope, envelope), TimeProvider.System, new InternalLedgerTreasuryBalanceGuard(Database, Company), missingWriter ? null : Writer).WithApprovalSafety();
        }
        public Task<ReversalProposalCommandResult> Decide(Guid proposal, bool approve, string key, string version = "1") => approve
            ? Service.ApproveAsync(new(CompanyA, ReceiptChecker, Correlation, null), new(key), new(proposal, version), default)
            : Service.RejectAsync(new(CompanyA, ReceiptChecker, Correlation, null), new(key), new(proposal, version, " SENTETIK ret "), default);
        public void AssertClean() { Assert.False(Database.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Database.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Database.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class ReceiptDecisionSafetyCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid value) => CompanyId = value; }
    private sealed class ReceiptDecisionSafetyUser : ICurrentUser
    { public string Actor = ReceiptChecker; public bool Member = true; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA, TestCompanyIds.CompanyB }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class ReceiptDecisionSafetyPermissions : IFinancePermissionPort
    { public Func<PermissionDecision>? Check; public Func<Task>? AfterRead;
      public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
      { var result = Check?.Invoke() ?? PermissionDecision.Allowed; if (AfterRead is not null) await AfterRead(); return result; } }
    private sealed class ReceiptDecisionSafetySave(Guid proposal) : SaveChangesInterceptor
    {
        public bool Saved, FailSave, Deadlock, Injected; public int Attempts; public Action? AfterSave;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is FinanceDbContext db && db.ChangeTracker.Entries<ReversalProposalRow>().Any(e => e.Entity.Id == proposal && e.State == EntityState.Modified))
            { Attempts++; if (FailSave) { Injected = true; if (Deadlock) throw new DbUpdateException("SENTETIK victim", SqlConcurrencyWrapperPolicyTests.SqlFailure(1205)); throw new DbUpdateConcurrencyException("SENTETIK race"); } }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) { Saved = true; AfterSave?.Invoke(); } return ValueTask.FromResult(result); }
    }
    private sealed class ReceiptDecisionSafetyRollbackFault : DbTransactionInterceptor
    { public int Attempts; public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
      { Attempts++; throw new InvalidOperationException("SENTETIK rollback failure"); } }
    private sealed class ReceiptDecisionSafetyCommitted : DbTransactionInterceptor
    { public int Commits; public Action? AfterCommit; public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default)
      { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; } }
}
