using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesCustomerReturnSqlServerRelationalAcceptanceTests
{
    [Fact]
    public async Task Actual_nonrelational_customer_return_guard_is_unavailable_and_never_discards_caller_audit()
    {
        var company = new CompanyContext(); company.Set(CompanyA); var collector = new ReturnRecordingCollector(); var writer = new ReturnEntryWriter(collector);
        await using var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseInMemoryDatabase("SENTETIK-return-guard-" + Guid.NewGuid()).Options, company);
        var store = new SalesStore(db, company, collector, TimeProvider.System, officialCancellationAuditWriter: writer); collector.Enqueue(ReturnCallerAudit());
        var envelope = new SalesDraftWriteEnvelope(Approver, SalesPermissionManifestV1.ReturnsApprove, [], "SENTETIK", null, "SENTETIK-entry", "Web", null, null); var executed = false;
        Assert.False(store.CustomerReturnDecisionEntryIsClean);
        await Assert.ThrowsAsync<SalesDraftStoreException>(() => store.RunCustomerReturnDecisionAsync(envelope, Hash('a'), SalesCustomerReturnCommands.ApproveV1, true,
            _ => { executed = true; throw new InvalidOperationException("No synthetic SQL success"); }, _ => Task.FromResult<SalesCustomerReturnCommandResult?>(null), _ => Task.CompletedTask, default));
        Assert.False(executed); Assert.Single(collector.Entries); Assert.Equal(0, writer.Writes); Assert.Equal(0, writer.Discards); Assert.Empty(db.ChangeTracker.Entries());
    }

    // Actual SQL owner + actual same-scoped Audit writer. Injected1205/1222 are controlled faults, not a real deadlock reproduction.
    [SqlServerFact]
    public async Task Owned_return_approve_and_reject_keep_native_raw_hash_audit_and_original_key_once()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-owned-normal");
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedReturn(run, invoice); var before = await ReturnEffects(run, id); var key = "owned-return-" + id.ToString("N");
            await using var safe = ReturnScope(run); var first = await DecideReturn(safe, id, key, approve); var replay = await DecideReturn(safe, id, key, approve);
            Assert.Equal((approve ? "Approved" : "Rejected", "3", false), (first.CustomerReturn.State, first.CustomerReturn.Version, first.IsReplay));
            Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(1, await ReturnAttempts(run, key));
            Assert.Equal(before with { State = approve ? 3 : 5, Version = 3, Audits = before.Audits + 1, Attempts = before.Attempts + 1 }, await ReturnEffects(run, id));
            var body = new SalesDraftStateCommandRequest("02", approve ? null : "  SENTETIK ret  "); var command = approve ? SalesCustomerReturnCommands.ApproveV1 : SalesCustomerReturnCommands.RejectV1;
            Assert.Equal(approve ? SalesCustomerReturnHash.Compute(command, id, body) : SalesCustomerReturnHash.ComputeDecision(command, id, body), await run.ScalarAsync<string>(
                "SELECT [RequestHash] FROM [sales].[CustomerReturnAttempts] WHERE [CommandName]=@command AND [IdempotencyKeyHash]=@key", ("@command", command), ("@key", SalesStore.IdempotencyKeyHash(key))));
            AssertReturnClean(safe);
        }
    }

    [SqlServerFact]
    public async Task Owned_return_native_maker_submitter_and_stale_version_refusals_are_durable_original_terminal_answers()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-owned-terminal");
        foreach (var actor in new[] { Preparer, "SENTETIK-return-submitter", Approver }) foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedReturn(run, invoice); var before = await ReturnEffects(run, id); var key = "terminal-return-" + id.ToString("N");
            await using var safe = ReturnScope(run, actor); var version = actor == Approver ? "1" : "2";
            Task<SalesCustomerReturnCommandResult> Decide() => DecideReturn(safe, id, key, approve, version: version);
            var first = await Assert.ThrowsAsync<SalesDraftServiceException>(Decide); var replay = await Assert.ThrowsAsync<SalesDraftServiceException>(Decide);
            Assert.Equal(409, first.Status); Assert.Equal(actor == Approver ? SalesDraftProblemCodesV1.VersionConflict : SalesCustomerReturnProblemCodesV1.SelfApproval, first.Code);
            Assert.False(first.IsReplay); Assert.True(replay.IsReplay); Assert.Equal(first.Code, replay.Code); Assert.Equal(first.Errors, replay.Errors);
            Assert.Null(first.OriginalCorrelationId); Assert.Equal(Metadata(key).CorrelationId, replay.OriginalCorrelationId); Assert.Equal(1, await ReturnAttempts(run, key));
            Assert.Equal(before with { Attempts = before.Attempts + 1 }, await ReturnEffects(run, id)); AssertReturnClean(safe);
        }
    }

    [SqlServerFact]
    public async Task Owned_return_saved_and_actual_precommit_fences_abort_authority_or_completed_cancellation_then_key_executes_once()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-owned-fences");
        foreach (var approve in new[] { true, false })
        {
            var phase = await ReturnPrecommitPhase(run, invoice, approve);
            foreach (var ordinal in new[] { 1, phase }) foreach (var permission in new[] { SalesPermissionManifestV1.ReturnsApprove, SalesPermissionManifestV1.OrdersPriceRead }) foreach (var cancel in new[] { true, false })
            {
                var id = await SeedOwnedReturn(run, invoice); var before = await ReturnEffects(run, id); var key = "abort-return-" + id.ToString("N");
                var save = new ReturnSaveProbe(); await using var safe = ReturnScope(run, Approver, save); using var cts = new CancellationTokenSource(); var count = 0; var fired = false;
                safe.Authority.AfterRead = p => { if (save.Saved && p == permission && ++count == ordinal) { fired = true; if (cancel) cts.Cancel(); else safe.Authority.Denied.Add(permission); } };
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DecideReturn(safe, id, key, approve, cts.Token));
                else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideReturn(safe, id, key, approve))).Status);
                Assert.True(fired); Assert.True(save.Saved); Assert.Equal(before, await ReturnEffects(run, id)); Assert.Equal(0, await ReturnAttempts(run, key)); AssertReturnClean(safe);
                await using var retry = ReturnScope(run); Assert.False((await DecideReturn(retry, id, key, approve)).IsReplay); Assert.True((await DecideReturn(retry, id, key, approve)).IsReplay);
                Assert.Equal(1, await ReturnAttempts(run, key));
            }
        }
    }

    [SqlServerFact]
    public async Task Owned_return_postcommit_release_loss_suppresses_reply_without_undoing_native_audit_or_original_key()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-owned-release");
        foreach (var permission in new[] { SalesPermissionManifestV1.ReturnsApprove, SalesPermissionManifestV1.OrdersPriceRead })
        {
            var id = await SeedOwnedReturn(run, invoice); var key = "release-return-" + id.ToString("N"); var commit = new ReturnCommitProbe();
            await using var safe = ReturnScope(run, Approver, commit); commit.AfterCommit = () => safe.Authority.Denied.Add(permission);
            Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideReturn(safe, id, key, true))).Status); Assert.Equal(1, commit.Commits);
            var durable = await ReturnEffects(run, id); Assert.Equal((3L, 3), (durable.Version, durable.State)); Assert.Equal(1, await ReturnAttempts(run, key));
            await using var retry = ReturnScope(run); var replay = await DecideReturn(retry, id, key, true); Assert.True(replay.IsReplay); Assert.Equal(Metadata(key).CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(durable, await ReturnEffects(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_return_preserves_dirty_pending_borrowed_and_initially_unsafe_caller_state()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-owned-caller"); var id = await SeedOwnedReturn(run, invoice); var before = await ReturnEffects(run, id);
        foreach (var kind in new[] { "dirty", "audit", "borrowed", "initial-dirty-cleaned", "initial-audit-cleaned" })
        {
            await using var safe = ReturnScope(run); var row = await safe.Db.CustomerReturns.SingleAsync(x => x.Id == id); var key = "caller-return-" + Guid.NewGuid().ToString("N");
            if (kind.Contains("dirty", StringComparison.Ordinal)) safe.Db.Entry(row).State = EntityState.Modified;
            if (kind.Contains("audit", StringComparison.Ordinal)) safe.Collector.Enqueue(ReturnCallerAudit());
            await using var borrowed = kind == "borrowed" ? await safe.Db.Database.BeginTransactionAsync() : null;
            var cleaned = false; safe.Authority.AfterRead = _ => { if (!cleaned && kind.StartsWith("initial-", StringComparison.Ordinal)) { cleaned = true; safe.Db.ChangeTracker.Clear(); safe.Writer.DiscardPending(); } };
            Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideReturn(safe, id, key, false))).Status);
            if (kind == "dirty") Assert.True(safe.Db.ChangeTracker.HasChanges()); if (kind == "audit") Assert.True(safe.Writer.HasPendingDrafts);
            if (kind == "borrowed") Assert.Same(borrowed, safe.Db.Database.CurrentTransaction);
            Assert.Equal(0, await ReturnAttempts(run, key)); Assert.Equal(before, await ReturnEffects(run, id));
            if (borrowed is not null) await borrowed.RollbackAsync();
        }
    }

    [SqlServerFact]
    public async Task Owned_return_refuses_an_ambient_transaction_without_taking_or_clearing_its_ownership()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-owned-ambient"); var id = await SeedOwnedReturn(run, invoice); var before = await ReturnEffects(run, id);
        var key = "ambient-return-" + id.ToString("N"); await using var safe = ReturnScope(run);
        using (var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var original = System.Transactions.Transaction.Current;
            Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideReturn(safe, id, key, true))).Status);
            Assert.Same(original, System.Transactions.Transaction.Current); Assert.Null(safe.Db.Database.CurrentTransaction); ambient.Complete();
        }
        // These verification connections are deliberately outside the caller ambient transaction.
        Assert.Equal(before, await ReturnEffects(run, id)); Assert.Equal(0, await ReturnAttempts(run, key));
    }

    [SqlServerFact]
    public async Task Owned_return_controlled_1205_and_1222_read_only_the_original_key_winner_and_never_retry_mutation()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-owned-race");
        foreach (var number in new[] { 1205, 1222 }) foreach (var winner in new[] { true, false })
        {
            var id = await SeedOwnedReturn(run, invoice); var before = await ReturnEffects(run, id); var key = "race-return-" + id.ToString("N");
            var query = new ReturnKeyProbe(); await using var loser = ReturnScope(run, Approver, query); SalesCustomerReturnCommandResult? original = null;
            query.First = async () => { if (winner) { await using var actual = ReturnScope(run); original = await DecideReturn(actual, id, key, true); } throw SqlConcurrencyWrapperPolicyTests.SqlFailure(number); };
            if (winner) { var replay = await DecideReturn(loser, id, key, true); Assert.NotNull(original); Assert.True(replay.IsReplay); Assert.Equal(original.OriginalCorrelationId, replay.OriginalCorrelationId); }
            else Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => DecideReturn(loser, id, key, true))).Status);
            Assert.Equal(2, query.Reads); Assert.Equal(winner ? 1 : 0, await ReturnAttempts(run, key)); AssertReturnClean(loser);
            Assert.Equal(winner ? before with { State = 3, Version = 3, Audits = before.Audits + 1, Attempts = before.Attempts + 1 } : before, await ReturnEffects(run, id));
        }
    }

    [SqlServerFact]
    public async Task Owned_return_cleanup_failure_prevents_readonly_reconciliation_and_preserves_original_failure()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-owned-cleanup"); var id = await SeedOwnedReturn(run, invoice); var before = await ReturnEffects(run, id);
        var key = "cleanup-return-" + id.ToString("N"); var save = new ReturnSaveProbe { Failure = SqlConcurrencyWrapperPolicyTests.SqlFailure(1222) }; var rollback = new ReturnRollbackFault(); var query = new ReturnKeyProbe();
        await using var safe = ReturnScope(run, Approver, save, rollback, query);
        var error = await Assert.ThrowsAsync<AggregateException>(() => DecideReturn(safe, id, key, false));
        Assert.Contains(error.Flatten().InnerExceptions, x => x.Message.Contains("SENTETIK rollback", StringComparison.Ordinal));
        Assert.Equal(1, rollback.Calls); Assert.Equal(1, save.Saves); Assert.Equal(2, query.Reads); // Initial native replay + native step lookup; no post-abort winner lookup.
        Assert.Equal(before, await ReturnEffects(run, id)); Assert.Equal(0, await ReturnAttempts(run, key));
    }

    [SqlServerFact]
    public async Task Owned_return_rejects_foreign_commands_and_foreign_owned_or_read_marker_legacy_return_writes()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-owned-nested"); var id = await SeedOwnedReturn(run, invoice); var before = await ReturnEffects(run, id);
        await using var safe = ReturnScope(run); var key = "nested-return-" + id.ToString("N"); var envelope = run.Envelope(key, Approver, SalesPermissionManifestV1.ReturnsApprove);
        var hash = Hash('a'); var refusal = new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, []); var entered = false;
        await Assert.ThrowsAsync<SalesDraftStoreException>(() => safe.Store.RunCustomerReturnDecisionAsync(envelope, hash, SalesCustomerReturnCommands.ApproveV1, true,
            async ct => { entered = true; await safe.Store.RecordCustomerReturnRefusalAsync(envelope, hash, SalesCustomerReturnCommands.CancelV1, id, refusal, ct); return null!; },
            _ => Task.FromResult<SalesCustomerReturnCommandResult?>(null), _ => Task.CompletedTask, default));
        Assert.True(entered); AssertReturnClean(safe);
        entered = false;
        await Assert.ThrowsAsync<SalesDraftStoreException>(() => safe.Store.RunRemainderDecisionAsync(envelope, hash, SalesDraftCommands.ApproveRemainderChangeV1, true,
            async ct => { entered = true; await safe.Store.RecordCustomerReturnRefusalAsync(envelope, hash, SalesCustomerReturnCommands.ApproveV1, id, refusal, ct); return null!; },
            _ => Task.FromResult<SalesOrderDraftResponse?>(null), _ => Task.CompletedTask, default));
        Assert.True(entered); AssertReturnClean(safe);
        entered = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.ReadRemainderDecisionSourceAsync(Guid.NewGuid(), async ct =>
        { entered = true; await safe.Store.RecordCustomerReturnRefusalAsync(envelope, hash, SalesCustomerReturnCommands.ApproveV1, id, refusal, ct); }));
        Assert.True(entered); Assert.Equal(before, await ReturnEffects(run, id)); Assert.Equal(0, await ReturnAttempts(run, key)); AssertReturnClean(safe);
    }

    private static async Task<Guid> SeedOwnedReturn(Run run, (SalesInvoice Invoice, Guid Shipment) source)
    {
        await using var raw = run.SalesStore(CompanyA); var service = run.Service(raw.Store, CompanyA, Preparer, null); var key = Guid.NewGuid().ToString("N");
        var first = await service.CreateCustomerReturnAsync(new(source.Invoice.Id, "IADE-OWNED-" + key, Day, [new(source.Invoice.Lines[0].Id, "1")], source.Shipment), Metadata("create-" + key));
        await run.Service(raw.Store, CompanyA, "SENTETIK-return-submitter", null).SubmitCustomerReturnAsync(first.CustomerReturn.Id, new("1"), Metadata("submit-" + key)); return first.CustomerReturn.Id;
    }
    private static Task<SalesCustomerReturnCommandResult> DecideReturn(ReturnSafetyScope safe, Guid id, string key, bool approve, CancellationToken token = default, string version = "02") => approve
        ? safe.Service.ApproveCustomerReturnAsync(id, new(version), Metadata(key), token) : safe.Service.RejectCustomerReturnAsync(id, new(version, "  SENTETIK ret  "), Metadata(key), token);
    private static async Task<int> ReturnPrecommitPhase(Run run, (SalesInvoice Invoice, Guid Shipment) invoice, bool approve)
    {
        var id = await SeedOwnedReturn(run, invoice); var save = new ReturnSaveProbe(); var commit = new ReturnCommitProbe(); await using var safe = ReturnScope(run, Approver, save, commit); var count = 0; var measured = 0;
        // Measure actual provider precommit after native SaveChanges; never infer the phase from a pre-receipt magic ordinal.
        safe.Authority.AfterRead = p => { if (save.Saved && p == SalesPermissionManifestV1.ReturnsApprove) count++; }; commit.BeforeCommit = () => measured = count;
        Assert.False((await DecideReturn(safe, id, "phase-return-" + id.ToString("N"), approve)).IsReplay); Assert.Equal(1, commit.Commits); Assert.True(measured > 1); return measured;
    }
    private static async Task<ReturnEffect> ReturnEffects(Run run, Guid id) => new(await run.ScalarAsync<int>("SELECT [State] FROM [sales].[CustomerReturns] WHERE [Id]=@id", ("@id", id)),
        await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[CustomerReturns] WHERE [Id]=@id", ("@id", id)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturnAttempts] WHERE [TargetId]=@id", ("@id", id)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType]=N'Sales.CustomerReturn' AND [EntityId]=@id", ("@id", id.ToString("D"))),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[OutboxMessages]"));
    private sealed record ReturnEffect(int State, long Version, int Attempts, int Audits, int Outbox);
    private static Task<int> ReturnAttempts(Run run, string key) => run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[CustomerReturnAttempts] WHERE [IdempotencyKeyHash]=@key", ("@key", SalesStore.IdempotencyKeyHash(key)));
    private static void AssertReturnClean(ReturnSafetyScope safe) { Assert.False(safe.Db.ChangeTracker.HasChanges()); Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction); }
    private static ReturnSafetyScope ReturnScope(Run run, string actor = Approver, params IInterceptor[] interceptors)
    {
        var scope = run.Factory.Services.CreateAsyncScope(); var p = scope.ServiceProvider; var company = p.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = p.GetRequiredService<AuditEventCollector>(); Assert.Same(collector, p.GetRequiredService<IAuditEventCollector>()); var writer = p.GetRequiredService<IAuditTransactionWriterV1>();
        var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, x => x.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).AddInterceptors(interceptors).Options, company);
        var store = new SalesStore(db, company, collector, TimeProvider.System, officialCancellationAuditWriter: writer); var authority = new ReturnSafetyAuthority(); var user = new FixedUser(actor, CompanyA);
        var service = new SalesCustomerReturnService(company, user, authority, new FixedCorrelation("SENTETIK-return-owned-original"), store, store, TimeProvider.System).WithCustomerReturnDecisionSafety();
        return new(scope, db, store, service, authority, writer, collector);
    }
    private sealed class ReturnSafetyScope(AsyncServiceScope scope, SalesDbContext db, SalesStore store, SalesCustomerReturnService service, ReturnSafetyAuthority authority, IAuditTransactionWriterV1 writer, AuditEventCollector collector) : IAsyncDisposable
    { internal SalesDbContext Db => db; internal SalesStore Store => store; internal SalesCustomerReturnService Service => service; internal ReturnSafetyAuthority Authority => authority; internal IAuditTransactionWriterV1 Writer => writer; internal AuditEventCollector Collector => collector;
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await scope.DisposeAsync(); } }
    private sealed class ReturnSafetyAuthority : IErpAuthorityRevalidationService
    { internal readonly HashSet<string> Denied = []; internal Action<string>? AfterRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken token = default)
        { Assert.Equal(CompanyA, c); await Task.Yield(); AfterRead?.Invoke(p); return new(!Denied.Contains(p), true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); } }
    private sealed class ReturnSaveProbe : SaveChangesInterceptor
    { internal bool Saved; internal int Saves; internal Exception? Failure;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default) { Saves++; if (Failure is not null) throw Failure; return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default) { Saved = true; return ValueTask.FromResult(result); } }
    private sealed class ReturnCommitProbe : DbTransactionInterceptor
    { internal int Commits; internal Action? BeforeCommit, AfterCommit;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction t, TransactionEventData d, InterceptionResult result, CancellationToken token = default) { BeforeCommit?.Invoke(); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction t, TransactionEndEventData d, CancellationToken token = default) { Commits++; AfterCommit?.Invoke(); return Task.CompletedTask; } }
    private sealed class ReturnRollbackFault : DbTransactionInterceptor
    { internal int Calls; public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction t, TransactionEventData d, InterceptionResult r, CancellationToken token = default) { Calls++; throw new InvalidOperationException("SENTETIK rollback fault"); } }
    private sealed class ReturnKeyProbe : DbCommandInterceptor
    { internal int Reads; internal Func<Task>? First;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<DbDataReader> r, CancellationToken token = default)
        { if (c.CommandText.Contains("CustomerReturnAttempts", StringComparison.Ordinal) && ++Reads == 1 && First is not null) await First(); return r; } }
    private sealed class ReturnRecordingCollector : IAuditEventCollector
    { internal readonly List<AuditEventDraft> Entries = []; public void Enqueue(AuditEventDraft entry) => Entries.Add(entry); }
    private sealed class ReturnEntryWriter(ReturnRecordingCollector collector) : IAuditTransactionWriterV1
    { internal int Writes, Discards; public bool HasPendingDrafts => collector.Entries.Count != 0;
        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken token = default) { Writes++; throw new InvalidOperationException("No SQL write in nonrelational entry proof"); }
        public void DiscardPending() { Discards++; collector.Entries.Clear(); } }
    private static AuditEventDraft ReturnCallerAudit() => new(CompanyA, "SENTETIK.return.caller.v1", "Sales.CustomerReturn", "SENTETIK", "Caller", null, null,
        AuditActorTypes.User, Approver, SalesPermissionManifestV1.ReturnsApprove, [], "{}", "{}", "SENTETIK caller", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK", null, "SENTETIK-return-caller");
}
