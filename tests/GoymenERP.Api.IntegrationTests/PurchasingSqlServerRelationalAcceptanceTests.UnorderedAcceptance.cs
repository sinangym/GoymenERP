using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Unordered_goods_and_service_occurrences_retain_original_receipts_without_orders_stock_or_Finance_effects()
    {
        await using var run = await Run.StartAsync(configureNumbers: false);
        foreach (var service in new[] { false, true })
        {
            var request = UnorderedSqlRequest(service); var envelope = UnorderedEnvelope(run, "SENTETIK-unordered-" + service);
            await using var scope = UnorderedSqlScope(run, CompanyA);
            var first = await RecordUnordered(scope, request, envelope);
            var replay = await scope.Store.RecordAsync(CompanyA, request, envelope with { CorrelationId = "SENTETIK-later" },
                UnorderedRequestHash(request), _ => throw new InvalidOperationException("Replay must not prepare"), _ => Task.CompletedTask, default);
            Assert.False(first.IsReplay); Assert.True(replay.IsReplay); Assert.Equal(envelope.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(first.Record.Source.Subject, replay.Record.Source.Subject);
            Assert.Equal(first.Record.Source.ControlEvidence, replay.Record.Source.ControlEvidence);
            Assert.Equal("10", replay.Record.Source.AcceptedQuantity); Assert.Equal("10", replay.Record.Source.UninvoicedQuantity);
            var source = await scope.Store.ReadAsync(CompanyA, request.AcceptanceId, request.AcceptanceLineId);
            Assert.Equal(PurchaseUnorderedAcceptanceSourceOutcomeV1.Found, source.Outcome);
            Assert.Equal(request.Subject, source.Source!.Subject); Assert.Equal(request.Evidence, source.Source.Evidence);
            var conflict = await Assert.ThrowsAsync<PurchasingStoreException>(() => RecordUnordered(scope,
                request with { AcceptedQuantity = "9" }, envelope));
            Assert.Equal(PurchasingStoreCodes.SameKeyPayloadMismatch, conflict.Code);
            await using var foreign = UnorderedSqlScope(run, CompanyB);
            Assert.Equal(PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound,
                (await foreign.Store.ReadAsync(CompanyB, request.AcceptanceId, request.AcceptanceLineId)).Outcome);
            Assert.Equal(PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound,
                (await foreign.Store.ReadAsync(CompanyA, request.AcceptanceId, request.AcceptanceLineId)).Outcome);
        }
        Assert.Equal((2, 2, 2), await UnorderedCounts(run));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoices"));
        // These tests invoke the real Purchasing/Audit owners. Supplier, unit, Documents and exception prerequisites
        // are synthetic prepare facts, not an operational admission or an Inventory/Finance acceptance claim.
    }

    [SqlServerFact]
    public async Task Unordered_actual_audit_append_then_authority_loss_rolls_back_facts_attempt_and_audit_and_allows_same_key()
    {
        await using var run = await Run.StartAsync(configureNumbers: false); var request = UnorderedSqlRequest(false);
        var envelope = UnorderedEnvelope(run, "SENTETIK-unordered-late"); var allowed = true; var appended = 0;
        await using (var scope = UnorderedSqlScope(run, CompanyA, afterAudit: () => { appended++; allowed = false; }))
        {
            Task Fence(CancellationToken _) => allowed ? Task.CompletedTask : throw new PurchasingServiceException(403, "forbidden");
            var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => RecordUnordered(scope, request, envelope, Fence));
            Assert.Equal(403, denied.Status); Assert.Equal(1, appended);
            Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
        }
        Assert.Equal((0, 0, 0), await UnorderedCounts(run));
        await using var retry = UnorderedSqlScope(run, CompanyA);
        Assert.False((await RecordUnordered(retry, request, envelope)).IsReplay);
        Assert.Equal((1, 1, 1), await UnorderedCounts(run));
    }

    [SqlServerFact]
    public async Task Unordered_actual_audit_failure_aborts_all_owned_rows_and_discards_owned_pending_state()
    {
        await using var run = await Run.StartAsync(configureNumbers: false); var request = UnorderedSqlRequest(true);
        var envelope = UnorderedEnvelope(run, "SENTETIK-unordered-audit-fail");
        await using (var scope = UnorderedSqlScope(run, CompanyA, failAudit: true))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => RecordUnordered(scope, request, envelope));
            Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.False(scope.Writer.HasPendingDrafts);
        }
        Assert.Equal((0, 0, 0), await UnorderedCounts(run));
        await using var retry = UnorderedSqlScope(run, CompanyA);
        await RecordUnordered(retry, request, envelope); Assert.Equal((1, 1, 1), await UnorderedCounts(run));
    }

    [SqlServerFact]
    public Task Unordered_borrowed_transaction_entry_refusal_preserves_the_callers_transaction() => UnorderedCallerState("borrowed");

    [SqlServerFact]
    public Task Unordered_dirty_entry_refusal_preserves_the_callers_tracked_work() => UnorderedCallerState("dirty");

    [SqlServerFact]
    public Task Unordered_pending_audit_entry_refusal_preserves_the_callers_actual_pending_draft() => UnorderedCallerState("audit");

    [SqlServerFact]
    public async Task Unordered_first_authority_await_cannot_adopt_or_discard_a_new_callers_pending_audit()
    {
        await using var run = await Run.StartAsync(configureNumbers: false); await using var scope = UnorderedSqlScope(run, CompanyA);
        var request = UnorderedSqlRequest(false); var checks = 0; var prepares = 0;
        Task Fence(CancellationToken _) { checks++; scope.Collector.Enqueue(VariancePendingAudit(run, request.AcceptanceId)); return Task.CompletedTask; }
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => scope.Store.RecordAsync(CompanyA, request,
            UnorderedEnvelope(run, "SENTETIK-unordered-await-caller"), UnorderedRequestHash(request), _ =>
            { prepares++; return Task.FromResult(UnorderedSqlRecord(request, run.Actor)); }, Fence, default));
        Assert.Equal(503, refusal.Status); Assert.Equal(1, checks); Assert.Equal(0, prepares);
        Assert.True(scope.Writer.HasPendingDrafts); Assert.Null(scope.Database.Database.CurrentTransaction);
        Assert.Empty(scope.Database.ChangeTracker.Entries()); Assert.Equal((0, 0, 0), await UnorderedCounts(run));
    }

    private static async Task UnorderedCallerState(string state)
    {
        await using var run = await Run.StartAsync(configureNumbers: false); await using var scope = UnorderedSqlScope(run, CompanyA);
        var request = UnorderedSqlRequest(false); var prepares = 0;
        var transaction = state == "borrowed" ? await scope.Database.Database.BeginTransactionAsync() : null;
        if (state == "dirty") scope.Database.UnorderedAcceptances.Add(new() { CompanyId = CompanyA, AcceptanceId = Guid.NewGuid(), AcceptanceLineId = Guid.NewGuid() });
        if (state == "audit") scope.Collector.Enqueue(VariancePendingAudit(run, request.AcceptanceId));
        var before = scope.Database.ChangeTracker.Entries().Select(x => (x.Entity, x.State)).ToArray();
        try
        {
            var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => scope.Store.RecordAsync(CompanyA, request,
                UnorderedEnvelope(run, "SENTETIK-unordered-caller"), UnorderedRequestHash(request), _ =>
                { prepares++; return Task.FromResult(UnorderedSqlRecord(request, run.Actor)); }, _ => Task.CompletedTask, default));
            Assert.Equal(503, denied.Status); Assert.Equal(0, prepares);
            Assert.Equal(before, scope.Database.ChangeTracker.Entries().Select(x => (x.Entity, x.State)).ToArray());
            Assert.Same(transaction, scope.Database.Database.CurrentTransaction);
            Assert.Equal(state == "audit", scope.Writer.HasPendingDrafts);
        }
        finally { if (transaction is not null) await transaction.DisposeAsync(); }
        Assert.Equal((0, 0, 0), await UnorderedCounts(run));
    }

    [SqlServerFact]
    public async Task Unordered_same_key_two_native_SQL_sessions_leave_one_occurrence_attempt_and_actual_audit()
    {
        await using var run = await Run.StartAsync(configureNumbers: false); var request = UnorderedSqlRequest(false);
        var envelope = UnorderedEnvelope(run, "SENTETIK-unordered-race"); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PurchaseUnorderedAcceptanceWriteV1> Write()
        { await using var scope = UnorderedSqlScope(run, CompanyA); await start.Task; return await RecordUnordered(scope, request, envelope); }
        var first = Write(); var second = Write(); start.SetResult(); var results = await Task.WhenAll(first, second);
        Assert.Single(results, x => x.IsReplay); Assert.Single(results, x => !x.IsReplay);
        Assert.All(results, x => Assert.Equal(envelope.CorrelationId, x.OriginalCorrelationId));
        Assert.Equal((1, 1, 1), await UnorderedCounts(run));
    }

    private static RecordPurchaseUnorderedAcceptanceV1 UnorderedSqlRequest(bool service) => new(Guid.NewGuid(), Guid.NewGuid(), "0",
        Guid.NewGuid(), Guid.NewGuid(), "supplier-1", "account-1", service
            ? new PurchaseUnorderedServiceSubjectV1(Guid.NewGuid(), "service-1") : new PurchaseUnorderedGoodsSubjectV1(Product, "product-1"),
        KgUnit, new(Guid.NewGuid(), 1), "10", T0, new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-actual-document-line"), []);
    private static PurchaseUnorderedAcceptanceRecordV1 UnorderedSqlRecord(RecordPurchaseUnorderedAcceptanceV1 r, string actor) => new(
        new(CompanyA, r.AcceptanceId, r.AcceptanceLineId, 1, r.SupplierPartyId, r.CompanyAccountId, r.Subject, r.UnitId,
            r.AcceptedQuantity, r.AcceptedQuantity, r.OccurredAtUtc, r.Evidence, r.ControlEvidence),
        r.ExpectedPartyVersion, r.ExpectedAccountVersion, r.UnitProfile, actor, T0);
    private static string UnorderedRequestHash(RecordPurchaseUnorderedAcceptanceV1 request) => PurchasingRequestHash.Compute(
        PurchaseUnorderedAcceptanceOwnerContractV1.RecordCommand, request.AcceptanceId, request);
    private static PurchasingWriteEnvelope UnorderedEnvelope(Run run, string key) => run.Envelope(key) with
    { PermissionCode = PurchasingPermissionManifestV1.GoodsReceiptsRecord };
    private static Task<PurchaseUnorderedAcceptanceWriteV1> RecordUnordered(UnorderedSqlOwner scope, RecordPurchaseUnorderedAcceptanceV1 request,
        PurchasingWriteEnvelope envelope, Func<CancellationToken, Task>? fence = null) => scope.Store.RecordAsync(CompanyA, request, envelope,
        UnorderedRequestHash(request), _ => Task.FromResult(UnorderedSqlRecord(request, envelope.ActorId)), fence ?? (_ => Task.CompletedTask), default);
    private static async Task<(int Facts, int Attempts, int Audits)> UnorderedCounts(Run run) =>
        (await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.UnorderedAcceptanceFacts"),
         await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.UnorderedAcceptanceAttempts"),
         await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'purchasing.unordered-acceptance.recorded.v1'"));
    private static UnorderedSqlOwner UnorderedSqlScope(Run run, Guid company, bool failAudit = false, Action? afterAudit = null)
    {
        var scope = run.Factory.Services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); context.Set(company);
        var database = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
        var collector = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        IAuditTransactionWriterV1 effective = failAudit ? new ServiceFailingAudit(writer)
            : afterAudit is null ? writer : new ServiceDecisionAuditWriter(writer, afterAudit);
        return new(scope, database, collector, effective, new(database, context, collector, effective));
    }
    private sealed class UnorderedSqlOwner(AsyncServiceScope scope, PurchasingDbContext database, IAuditEventCollector collector,
        IAuditTransactionWriterV1 writer, PurchaseUnorderedAcceptanceStoreV1 store) : IAsyncDisposable
    {
        public PurchasingDbContext Database => database; public IAuditEventCollector Collector => collector;
        public IAuditTransactionWriterV1 Writer => writer; public PurchaseUnorderedAcceptanceStoreV1 Store => store;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
