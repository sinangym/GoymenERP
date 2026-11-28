using System.Data;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// Real SQL fixture/native commands/actual scoped Audit writer. Provider errors below are controlled injections,
// not a simultaneous native SQL deadlock. This partial has not been executed at its source head.
public sealed partial class SalesContractSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Owned_contract_discount_native_commands_keep_raw_hash_reason_attempt_audit_and_original_key_replay()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var pending = await run.PendingContractAsync("contract-owned-seed-" + approve); var key = "contract-owned-" + pending.Id.ToString("N");
            var body = new SalesDraftStateCommandRequest("01", approve ? "legacy ignored approval note" : "  SENTETIK raw ret\n  ");
            var save = new ContractSaveProbe(); await using var safe = ContractSafetyScope(run, save);
            var first = await ContractDecision(safe, pending.Id, key, approve, body);
            Assert.Equal((approve ? "Active" : "DiscountRejected", "2", false, "SENTETIK-contract-original"), (first.Contract.State, first.Contract.Version, first.IsReplay, first.OriginalCorrelationId));
            Assert.Equal(approve ? null : body.Reason!.Trim(), first.Contract.RejectionReason); Assert.Equal(1, save.Saves);
            var command = approve ? SalesContractCommands.ApproveDiscountV1 : SalesContractCommands.RejectDiscountV1;
            Assert.Equal(SalesContractHash.Compute(command, pending.Id, body), await run.ScalarAsync<string>("SELECT RequestHash FROM sales.ContractAttempts WHERE ContractId=@id AND CommandName=@cmd", ("@id", pending.Id), ("@cmd", command)));
            Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id)); AssertContractSafetyClean(safe);
            var replay = await ContractDecision(safe, pending.Id, key, approve, body, correlation: "SENTETIK later request");
            Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(JsonSerializer.Serialize(first.Contract), JsonSerializer.Serialize(replay.Contract)); Assert.Equal(1, save.Saves);
            var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, key, approve, body with { ExpectedVersion = "1" }));
            Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (mismatch.Status, mismatch.Code));
            Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
        }
    }

    [SqlServerFact]
    public async Task Owned_contract_native_self_stale_and_invalid_reason_keep_terminal_receipt_policy()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var self in new[] { true, false })
        {
            var pending = await run.PendingContractAsync("contract-terminal-" + Guid.NewGuid().ToString("N"));
            await using var safe = ContractSafetyScope(run, actor: self ? Seller : Manager); var key = "terminal-" + pending.Id.ToString("N");
            var body = new SalesDraftStateCommandRequest(self ? "1" : "02", approve ? null : "  native ret  ");
            var first = await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, key, approve, body));
            Assert.Equal((409, self ? SalesContractProblemCodesV1.SelfDecision : SalesDraftProblemCodesV1.VersionConflict), (first.Status, first.Code)); Assert.False(first.IsReplay);
            var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, key, approve, body));
            Assert.True(replay.IsReplay); Assert.Equal((first.Status, first.Code, "SENTETIK-contract-original"), (replay.Status, replay.Code, replay.OriginalCorrelationId));
            Assert.Equal((1, 0, 1L), await ContractDecisionFacts(run, pending.Id)); AssertContractSafetyClean(safe);
        }
        var invalid = await run.PendingContractAsync("contract-invalid-reason"); await using var scope = ContractSafetyScope(run);
        Assert.Equal(422, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(scope, invalid.Id, "reusable-invalid", false, new("1", " ")))).Status);
        Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, invalid.Id)); AssertContractSafetyClean(scope);
        Assert.False((await ContractDecision(scope, invalid.Id, "reusable-invalid", false, new("1", "  corrected ret  "))).IsReplay);
        Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, invalid.Id));
    }

    [SqlServerFact]
    public async Task Owned_contract_actual_audit_flush_revocation_rolls_back_before_any_entity_save()
    {
        await using var run = await Run.StartAsync(); var pending = await run.PendingContractAsync("contract-audit-phase"); var save = new ContractSaveProbe();
        await using var safe = ContractSafetyScope(run, save); var seenPending = false; var fired = false;
        safe.Authority.AfterRead = _ =>
        {
            if (safe.Writer.HasPendingDrafts) seenPending = true;
            if (seenPending && safe.Writer.Flushes == 1 && !safe.Writer.HasPendingDrafts) { fired = true; safe.Authority.Granted = false; }
            return Task.CompletedTask;
        };
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, "audit-phase", true))).Status);
        Assert.True(seenPending); Assert.True(fired); Assert.Equal(1, safe.Writer.Flushes); Assert.Equal(0, save.Saves);
        Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id)); AssertContractSafetyClean(safe);
        safe.Authority.AfterRead = null; safe.Authority.Granted = true;
        Assert.False((await ContractDecision(safe, pending.Id, "audit-phase", true)).IsReplay); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
    }

    [SqlServerFact]
    public async Task Owned_contract_postsave_and_precommit_authority_loss_or_cancel_rolls_back_then_same_key_decides_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var phase in new[] { 1, 3 }) foreach (var cancel in new[] { true, false })
        {
            var pending = await run.PendingContractAsync("contract-late-" + Guid.NewGuid().ToString("N")); var key = "late-" + pending.Id.ToString("N"); var save = new ContractSaveProbe();
            using var cancellation = new CancellationTokenSource(); await using var safe = ContractSafetyScope(run, save); var savedFences = 0; var fired = false;
            safe.Authority.AfterRead = permission =>
            {
                // After SavedChanges:1=postSave,2=post native result,3=final precommit. Optional price reads are not authority phases.
                if (permission == SalesPermissionManifestV1.ContractsDiscountApprove && save.Saved && ++savedFences == phase)
                { fired = true; if (cancel) cancellation.Cancel(); else safe.Authority.Granted = false; }
                return Task.CompletedTask;
            };
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContractDecision(safe, pending.Id, key, approve, token: cancellation.Token));
            else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, key, approve))).Status);
            Assert.True(fired); Assert.Equal(1, save.Saves); Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id)); AssertContractSafetyClean(safe);
            safe.Authority.AfterRead = null; safe.Authority.Granted = true;
            Assert.False((await ContractDecision(safe, pending.Id, key, approve)).IsReplay); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
        }
    }

    [SqlServerFact]
    public async Task Owned_contract_postcommit_permission_actor_membership_or_cancel_suppresses_response_without_undoing_decision()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false }) foreach (var fault in new[] { "permission", "actor", "membership", "cancel" })
        {
            var pending = await run.PendingContractAsync("contract-release-" + Guid.NewGuid().ToString("N")); var key = "release-" + pending.Id.ToString("N"); var save = new ContractSaveProbe(); var commit = new ContractCommitProbe();
            using var cancellation = new CancellationTokenSource(); await using var safe = ContractSafetyScope(run, save, commit); var fired = false;
            commit.AfterCommit = () => { fired = true; if (fault == "permission") safe.Authority.Granted = false; if (fault == "actor") safe.User.Actor = "SENTETIK-other"; if (fault == "membership") safe.User.Member = false; if (fault == "cancel") cancellation.Cancel(); };
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContractDecision(safe, pending.Id, key, approve, token: cancellation.Token));
            else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, key, approve))).Status);
            Assert.True(fired); Assert.Equal(1, commit.Commits); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id)); AssertContractSafetyClean(safe);
            commit.AfterCommit = null; safe.Authority.Granted = true; safe.User.Actor = Manager; safe.User.Member = true;
            var replay = await ContractDecision(safe, pending.Id, key, approve, correlation: "SENTETIK restored authority");
            Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-contract-original", replay.OriginalCorrelationId); Assert.Equal(1, save.Saves); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
        }
    }

    [SqlServerFact]
    public async Task Owned_contract_entry_preserves_caller_dirty_audit_borrowed_ambient_and_first_await_changes()
    {
        await using var run = await Run.StartAsync(); var pending = await run.PendingContractAsync("contract-caller");
        foreach (var fault in new[] { "dirty", "audit", "borrowed", "ambient", "late-dirty", "late-audit", "dirty-clean", "audit-clean", "missing-writer" })
        {
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var reads = new ContractReadProbe(); await using var safe = ContractSafetyScope(run, reads, withWriter: fault != "missing-writer");
            var caller = new DocumentCounterRow { CompanyId = CompanyA, Series = "SENTETIK-CT", Next = 1, Version = 1, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await using var borrowed = fault == "borrowed" ? await safe.Db.Database.BeginTransactionAsync() : null;
            if (fault.StartsWith("dirty", StringComparison.Ordinal)) safe.Db.DocumentCounters.Add(caller);
            if (fault.StartsWith("audit", StringComparison.Ordinal)) safe.Collector.Enqueue(ContractCallerAudit());
            var changed = false;
            safe.Authority.AfterRead = _ =>
            {
                if (changed) return Task.CompletedTask;
                if (fault == "late-dirty") { safe.Db.DocumentCounters.Add(caller); changed = true; }
                if (fault == "late-audit") { safe.Collector.Enqueue(ContractCallerAudit()); changed = true; }
                if (fault == "dirty-clean") { safe.Db.ChangeTracker.Clear(); changed = true; }
                if (fault == "audit-clean") { safe.Writer.DiscardPending(); changed = true; }
                return Task.CompletedTask;
            };
            Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, "caller-" + fault, true))).Status);
            Assert.Equal(0, reads.KeyReads); Assert.Equal(0, safe.Writer.Flushes); Assert.Equal(fault == "audit-clean" ? 1 : 0, safe.Writer.Discards);
            if (fault is "dirty" or "late-dirty") Assert.Same(caller, Assert.Single(safe.Db.ChangeTracker.Entries()).Entity);
            if (fault is "audit" or "late-audit") Assert.True(safe.Writer.HasPendingDrafts);
            if (borrowed is not null) { Assert.Same(borrowed, safe.Db.Database.CurrentTransaction); await borrowed.RollbackAsync(); }
            if (ambient is not null) Assert.NotNull(System.Transactions.Transaction.Current);
            safe.Db.ChangeTracker.Clear(); safe.Writer.DiscardPending();
        }
        Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id));
    }

    [SqlServerFact]
    public async Task Owned_contract_attempt_read_revalidation_stops_before_contract_query_or_save()
    {
        await using var run = await Run.StartAsync(); var pending = await run.PendingContractAsync("contract-read-stop"); var reads = new ContractReadProbe(); var save = new ContractSaveProbe(); await using var safe = ContractSafetyScope(run, reads, save);
        reads.AfterFirst = () => safe.Authority.Granted = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, "read-stop", true))).Status);
        Assert.Equal(1, reads.KeyReads); Assert.Equal(0, reads.ContractReads); Assert.Equal(0, save.Saves); AssertContractSafetyClean(safe); Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id));
    }

    [SqlServerFact]
    public async Task Owned_contract_saved_domain_fault_cannot_become_a_false_terminal_receipt_and_key_remains_available()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var pending = await run.PendingContractAsync("contract-saved-domain-" + approve); var save = new ContractSaveProbe { AfterSaved = () => throw new SalesRuleException(SalesRuleCodes.StateConflict, "SENTETIK after EF accepted changes") };
            await using (var safe = ContractSafetyScope(run, save))
            {
                Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, "saved-domain", approve))).Status);
                Assert.Equal(1, save.Saves); Assert.True(save.Saved); AssertContractSafetyClean(safe);
            }
            Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id)); await using var retry = ContractSafetyScope(run);
            Assert.False((await ContractDecision(retry, pending.Id, "saved-domain", approve)).IsReplay); Assert.True((await ContractDecision(retry, pending.Id, "saved-domain", approve)).IsReplay); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
        }
    }

    [SqlServerFact]
    public async Task Controlled_contract_final_save_races_clean_abort_then_read_only_original_key_without_second_mutation()
    {
        await using var run = await Run.StartAsync();
        foreach (var number in new[] { 0, 1205, 1222, 2601, 2627 }) foreach (var winner in new[] { false, true })
        {
            var pending = await run.PendingContractAsync("contract-save-race-" + Guid.NewGuid().ToString("N")); var key = "race-" + pending.Id.ToString("N");
            var save = new ContractSaveProbe { BeforeSave = () => throw (number == 0 ? new DbUpdateConcurrencyException("SENTETIK controlled concurrency") : new DbUpdateException("SENTETIK controlled provider race", SqlConcurrencyWrapperPolicyTests.SqlFailure(number))) };
            await using var safe = ContractSafetyScope(run, save); var supplied = false;
            safe.Authority.AfterRead = async _ =>
            {
                if (safe.Writer.Discards == 0 || supplied || !winner) return; supplied = true;
                Assert.Null(safe.Db.Database.CurrentTransaction); Assert.Empty(safe.Db.ChangeTracker.Entries());
                await using var winning = ContractSafetyScope(run); Assert.False((await ContractDecision(winning, pending.Id, key, true, correlation: "SENTETIK actual winner")).IsReplay);
            };
            if (winner) { var result = await ContractDecision(safe, pending.Id, key, true); Assert.True(result.IsReplay); Assert.Equal("SENTETIK actual winner", result.OriginalCorrelationId); }
            else Assert.Equal(number is 0 or 2601 or 2627 ? 409 : 503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, key, true))).Status);
            Assert.Equal(1, save.Saves); AssertContractSafetyClean(safe); Assert.Equal(winner ? (1, 1, 2L) : (0, 0, 1L), await ContractDecisionFacts(run, pending.Id));
        }
    }

    [SqlServerFact]
    public async Task Controlled_contract_original_key_read_1205_or_1222_has_one_readonly_pass_and_no_mutation_retry()
    {
        await using var run = await Run.StartAsync();
        foreach (var number in new[] { 1205, 1222 }) foreach (var winner in new[] { false, true })
        {
            var pending = await run.PendingContractAsync("contract-read-race-" + Guid.NewGuid().ToString("N")); var key = "read-race-" + pending.Id.ToString("N");
            if (winner) { await using var seed = ContractSafetyScope(run); await ContractDecision(seed, pending.Id, key, true); }
            var reads = new ContractReadProbe { BeforeFirst = () => throw SqlConcurrencyWrapperPolicyTests.SqlFailure(number) }; var save = new ContractSaveProbe(); await using var safe = ContractSafetyScope(run, reads, save);
            if (winner) Assert.True((await ContractDecision(safe, pending.Id, key, true)).IsReplay);
            else Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, key, true))).Status);
            Assert.Equal(2, reads.KeyReads); Assert.Equal(0, save.Saves); AssertContractSafetyClean(safe); Assert.Equal(winner ? (1, 1, 2L) : (0, 0, 1L), await ContractDecisionFacts(run, pending.Id));
        }
    }

    [SqlServerFact]
    public async Task Controlled_contract_completed_provider_1205_skips_only_redundant_rollback_and_preserves_original_key()
    {
        await using var run = await Run.StartAsync(); var pending = await run.PendingContractAsync("contract-completed-1205"); var reads = new ContractReadProbe(); var save = new ContractSaveProbe(); await using var safe = ContractSafetyScope(run, reads, save);
        reads.BeforeFirst = () => { var provider = safe.Db.Database.CurrentTransaction!.GetDbTransaction(); provider.Rollback(); Assert.Null(provider.Connection); throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); };
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, "completed-provider", true))).Status);
        Assert.Equal(2, reads.KeyReads); Assert.Equal(0, save.Saves); AssertContractSafetyClean(safe); Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id));
        reads.BeforeFirst = null; Assert.False((await ContractDecision(safe, pending.Id, "completed-provider", true)).IsReplay); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
    }

    [SqlServerFact]
    public async Task Owned_contract_cleanup_fault_remains_aggregate_and_never_reads_a_winner_or_records_a_false_refusal()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "rollback", "dispose" })
        {
            var pending = await run.PendingContractAsync("contract-cleanup-fault-" + phase); var cleanup = new ContractCleanupProbe(phase) { Fail = true }; var reads = new ContractReadProbe(); var save = new ContractSaveProbe { AfterSaved = () => throw new SalesRuleException(SalesRuleCodes.StateConflict, "SENTETIK saved domain fault") };
            await using var safe = ContractSafetyScope(run, cleanup, reads, save);
            var failure = await Assert.ThrowsAsync<AggregateException>(() => ContractDecision(safe, pending.Id, "cleanup-fault", true)); Assert.NotEmpty(failure.InnerExceptions); Assert.True(cleanup.Observed > 0); Assert.Equal(1, save.Saves);
            Assert.Equal(2, reads.KeyReads); Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id));
        }
    }

    [SqlServerFact]
    public async Task Owned_contract_cleanup_release_revocation_or_cancel_replaces_only_the_unrecorded_native_validation_answer()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { "rollback", "dispose" }) foreach (var cancel in new[] { false, true })
        {
            var pending = await run.PendingContractAsync("contract-cleanup-release-" + Guid.NewGuid().ToString("N")); var key = "validation-release-" + pending.Id.ToString("N"); var cleanup = new ContractCleanupProbe(phase); using var cancellation = new CancellationTokenSource(); await using var safe = ContractSafetyScope(run, cleanup);
            cleanup.AfterCleanup = () => { if (cancel) cancellation.Cancel(); else safe.Authority.Granted = false; };
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContractDecision(safe, pending.Id, key, false, new("1", " "), token: cancellation.Token));
            else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => ContractDecision(safe, pending.Id, key, false, new("1", " ")))).Status);
            Assert.True(cleanup.Observed > 0); Assert.Equal((0, 0, 1L), await ContractDecisionFacts(run, pending.Id)); AssertContractSafetyClean(safe);
            cleanup.AfterCleanup = null; safe.Authority.Granted = true; Assert.False((await ContractDecision(safe, pending.Id, key, false)).IsReplay); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
        }
    }

    [SqlServerFact]
    public async Task Owned_contract_late_optional_price_read_withholds_every_amount_and_rate_without_general_read_grant()
    {
        await using var run = await Run.StartAsync();
        foreach (var lateGrant in new[] { true, false })
        {
            var pending = await run.PendingContractAsync("contract-price-release-" + lateGrant); var key = "price-release-" + pending.Id.ToString("N"); var commit = new ContractCommitProbe(); await using var safe = ContractSafetyScope(run, commit); safe.Authority.PriceGranted = !lateGrant;
            safe.Authority.AfterRead = permission => { if (permission == SalesPermissionManifestV1.OrdersPriceRead && safe.Authority.PriceReads == 3) safe.Authority.PriceGranted = lateGrant; return Task.CompletedTask; };
            var result = await ContractDecision(safe, pending.Id, key, true); Assert.Equal(1, commit.Commits); Assert.Equal(SalesPriceDisclosure.Withheld, result.Contract.AmountDisclosure);
            Assert.Null(result.Contract.PlannedTotalAmount); Assert.Null(result.Contract.DiscountLimitPercent); Assert.All(result.Contract.Installments, x => Assert.Null(x.Amount)); Assert.All(result.Contract.DiscountRules, x => Assert.Null(x.RatePercent));
            Assert.Equal(3, safe.Authority.PriceReads); Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead, safe.Authority.Asked[^1]); Assert.DoesNotContain(SalesPermissionManifestV1.ContractsRead, safe.Authority.Asked); Assert.Equal((1, 1, 2L), await ContractDecisionFacts(run, pending.Id));
        }
    }

    private static Task<SalesContractCommandResult> ContractDecision(ContractSafetyState s, Guid id, string key, bool approve,
        SalesDraftStateCommandRequest? body = null, CancellationToken token = default, string correlation = "SENTETIK-contract-original")
    {
        body ??= new("1", approve ? null : "  SENTETIK raw ret  "); var metadata = new SalesCommandMetadata(key, correlation, null, DateTimeOffset.UtcNow);
        return approve ? s.Service.ApproveContractDiscountAsync(id, body, metadata, token) : s.Service.RejectContractDiscountAsync(id, body, metadata, token);
    }
    private static async Task<(int Attempts, int Audit, long Version)> ContractDecisionFacts(Run run, Guid id) =>
        (await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.ContractAttempts WHERE ContractId=@id AND CommandName IN (N'sales.contract.discount-approve.v1',N'sales.contract.discount-reject.v1')", ("@id", id)),
         await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType IN (N'sales.contract.discount-approved.v1',N'sales.contract.discount-rejected.v1')", ("@id", id.ToString("D"))),
         await run.ScalarAsync<long>("SELECT Version FROM sales.Contracts WHERE Id=@id", ("@id", id)));
    private static void AssertContractSafetyClean(ContractSafetyState s)
    {
        Assert.DoesNotContain(s.Db.ChangeTracker.Entries(), e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        Assert.False(s.Writer.HasPendingDrafts); Assert.Null(s.Db.Database.CurrentTransaction);
    }
    private static ContractSafetyState ContractSafetyScope(Run run, IInterceptor? first = null, IInterceptor? second = null,
        IInterceptor? third = null, string actor = Manager, bool withWriter = true)
    {
        var scope = run.Factory.Services.CreateAsyncScope(); var p = scope.ServiceProvider; var company = p.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = p.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, p.GetRequiredService<IAuditEventCollector>());
        var actual = p.GetRequiredService<IAuditTransactionWriterV1>(); Assert.Same(actual, p.GetRequiredService<IAuditTransactionWriterV1>()); var writer = new ContractWriter(actual);
        var options = new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
        foreach (var interceptor in new[] { first, second, third }) if (interceptor is not null)
        { options.AddInterceptors(interceptor); if (interceptor is ContractCleanupProbe cleanup) cleanup.Configure(options); }
        var db = new SalesDbContext(options.Options, company); var store = new SalesStore(db, company, collector, TimeProvider.System, officialCancellationAuditWriter: withWriter ? writer : null);
        var authority = new ContractAuthority(); var user = new ContractUser(actor);
        // Decisions use retained native contract facts; no external account/Settings admission is called here.
        var service = new SalesContractService(company, user, authority, new ContractCorrelation(), store, null!, null!, TimeProvider.System).WithContractDiscountDecisionSafety();
        return new(scope, db, service, authority, user, writer, collector);
    }
    private sealed class ContractSafetyState(AsyncServiceScope scope, SalesDbContext db, SalesContractService service,
        ContractAuthority authority, ContractUser user, ContractWriter writer, AuditEventCollector collector) : IAsyncDisposable
    {
        internal SalesDbContext Db => db; internal SalesContractService Service => service; internal ContractAuthority Authority => authority;
        internal ContractUser User => user; internal ContractWriter Writer => writer; internal AuditEventCollector Collector => collector;
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await scope.DisposeAsync(); }
    }
    private sealed class ContractUser(string actor) : ICurrentUser
    {
        internal string Actor = actor; internal bool Member = true;
        public bool IsAuthenticated => true; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => Member ? new HashSet<Guid>([CompanyA]) : new HashSet<Guid>();
        public bool CanAccessCompany(Guid id) => Member && id == CompanyA;
    }
    private sealed class ContractAuthority : IErpAuthorityRevalidationService
    {
        internal bool Granted = true; internal bool PriceGranted = true; internal int PriceReads; internal List<string> Asked = []; internal Func<string, Task>? AfterRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken cancellationToken = default)
        {
            Assert.Equal(CompanyA, company); Asked.Add(permission); if (permission == SalesPermissionManifestV1.OrdersPriceRead) PriceReads++;
            await Task.Yield(); if (AfterRead is not null) await AfterRead(permission);
            return new(permission == SalesPermissionManifestV1.OrdersPriceRead ? PriceGranted : Granted, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    private sealed class ContractCorrelation : ICorrelationContext
    {
        public bool IsResolved => true; public string CorrelationId => "SENTETIK-contract-original"; public string? CausationId => null;
        public string SourceChannel => "test"; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string id, string? cause, string channel, string? ip, string? agent) => throw new NotSupportedException();
    }
    // Telemetry only: all audit writes/discards still go through the actual same-scope writer/collector.
    private sealed class ContractWriter(IAuditTransactionWriterV1 actual) : IAuditTransactionWriterV1
    {
        internal int Flushes; internal int Discards; public bool HasPendingDrafts => actual.HasPendingDrafts;
        public async Task WritePendingAsync(IDbTransaction tx, CancellationToken token = default) { await actual.WritePendingAsync(tx, token); Flushes++; }
        public void DiscardPending() { Discards++; actual.DiscardPending(); }
    }
    private sealed class ContractSaveProbe : SaveChangesInterceptor
    {
        internal int Saves; internal bool Saved; internal Action? BeforeSave; internal Action? AfterSaved;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Saves++; BeforeSave?.Invoke(); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { Saved = true; AfterSaved?.Invoke(); return ValueTask.FromResult(result); }
    }
    private sealed class ContractCommitProbe : DbTransactionInterceptor
    {
        internal int Commits; internal Action? AfterCommit;
        public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default)
        { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class ContractReadProbe : DbCommandInterceptor
    {
        internal int KeyReads; internal int ContractReads; internal Action? BeforeFirst; internal Action? AfterFirst;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal) && command.CommandText.Contains("FROM [sales].[ContractAttempts]", StringComparison.Ordinal) && ++KeyReads == 1) BeforeFirst?.Invoke();
            if (command.CommandText.Contains("FROM [sales].[Contracts]", StringComparison.Ordinal)) ContractReads++;
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default)
        { if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal) && command.CommandText.Contains("FROM [sales].[ContractAttempts]", StringComparison.Ordinal) && KeyReads == 1) AfterFirst?.Invoke(); return ValueTask.FromResult(result); }
    }
    private sealed class ContractCleanupProbe(string phase) : DbTransactionInterceptor
    {
        internal bool Fail; internal int Observed; internal Action? AfterCleanup;
        internal void Configure(DbContextOptionsBuilder<SalesDbContext> options) => options.LogTo(
            (id, _) => id == RelationalEventId.TransactionDisposed, _ => { if (phase == "dispose") Changed(); });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (phase == "rollback" && Fail) Changed(); return ValueTask.FromResult(result); }
        public override Task TransactionRolledBackAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default)
        { if (phase == "rollback" && !Fail) Changed(); return Task.CompletedTask; }
        private void Changed() { if (Observed++ != 0) return; AfterCleanup?.Invoke(); if (Fail) throw new InvalidOperationException("SENTETIK controlled contract " + phase + " fault"); }
    }
    private static AuditEventDraft ContractCallerAudit() => new(CompanyA, "SENTETIK.contract.caller.v1", "Sales.Contract", "SENTETIK", "Caller", null, null,
        AuditActorTypes.User, Manager, SalesPermissionManifestV1.ContractsDiscountApprove, [], "{}", "{}", "SENTETIK caller", null, AuditAiInvolvement.None,
        null, "test", null, null, "SENTETIK-caller", null, "SENTETIK-caller-key");
}
