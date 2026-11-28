using System.Data.Common;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Requests;
using GoymenERP.Modules.Sales.Domain.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesCustomerReturnSqlServerRelationalAcceptanceTests
{
    // Source-only SQL scenarios: actual ReturnScope/store/Audit pair; no constructed success receipt.
    [SqlServerFact]
    public async Task Return_owned_initial_fresh_callback_cannot_create_a_native_request_or_quote_before_any_IO()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-foreign-initial");
        var id = await SeedOwnedReturn(run, invoice); var before = await ForeignNativeCounts(run); var probe = new ForeignMutationCommandProbe();
        await using var safe = ReturnScope(run, Approver, probe); var fired = false;
        var answer = await RunReturnWithForeignCallback(run, safe, id, "foreign-initial-" + id.ToString("N"), async ct =>
        {
            if (fired || safe.Db.Database.CurrentTransaction is null) return;
            fired = true; await AssertForeignCreatesBlocked(run, safe, probe, ct);
        });
        Assert.True(fired); Assert.False(answer.IsReplay); Assert.Equal("Approved", answer.CustomerReturn.State);
        Assert.Equal(before, await ForeignNativeCounts(run)); AssertReturnClean(safe);
    }

    [SqlServerFact]
    public async Task Return_owned_native_postcommit_release_callback_still_blocks_foreign_native_creates_without_a_current_TX()
    {
        await using var run = await Run.StartAsync(); var invoice = await run.FinalizedInvoiceAsync("return-foreign-release");
        var id = await SeedOwnedReturn(run, invoice); var before = await ForeignNativeCounts(run); var probe = new ForeignMutationCommandProbe();
        await using var safe = ReturnScope(run, Approver, probe); var sawOwnedTransaction = false; var fired = false;
        var answer = await RunReturnWithForeignCallback(run, safe, id, "foreign-release-" + id.ToString("N"), async ct =>
        {
            if (safe.Db.Database.CurrentTransaction is not null) { sawOwnedTransaction = true; return; }
            if (!sawOwnedTransaction || fired) return;
            fired = true; await AssertForeignCreatesBlocked(run, safe, probe, ct);
        });
        Assert.True(sawOwnedTransaction); Assert.True(fired); Assert.False(answer.IsReplay);
        Assert.Equal(before, await ForeignNativeCounts(run)); AssertReturnClean(safe);
        Assert.Equal(1, await ReturnAttempts(run, "foreign-release-" + id.ToString("N")));
    }

    [SqlServerFact]
    public async Task Selected_quote_and_remainder_read_callbacks_cannot_create_a_native_request_or_quote()
    {
        await using var run = await Run.StartAsync(); var before = await ForeignNativeCounts(run);
        foreach (var quote in new[] { true, false })
        {
            var probe = new ForeignMutationCommandProbe(); await using var safe = ReturnScope(run, Approver, probe); var fired = 0;
            async Task Fence(CancellationToken ct) { fired++; await AssertForeignCreatesBlocked(run, safe, probe, ct); }
            if (quote) Assert.Null(await safe.Store.ReadQuoteConversionDecisionAsync(Guid.NewGuid(), Fence));
            else Assert.Null(await safe.Store.ReadRemainderDecisionSourceAsync(Guid.NewGuid(), Fence));
            Assert.True(fired > 0); AssertReturnClean(safe); Assert.Equal(before, await ForeignNativeCounts(run));
        }
    }

    [SqlServerFact]
    public async Task No_marker_native_request_and_quote_creates_and_original_key_replays_are_unchanged()
    {
        await using var run = await Run.StartAsync(); var before = await ForeignNativeCounts(run);
        await using var safe = ReturnScope(run); var requestId = Guid.NewGuid(); var quoteId = Guid.NewGuid(); var requestBuilds = 0; var quoteBuilds = 0;
        var requestEnvelope = run.Envelope("foreign-positive-request", Approver, SalesPermissionManifestV1.RequestsWrite);
        var quoteEnvelope = run.Envelope("foreign-positive-quote", Approver, SalesPermissionManifestV1.QuotesWrite);
        SalesRequest Request(string number) { requestBuilds++; return ForeignNativeRequest(requestId, number); }
        SalesQuote Quote(string number) { quoteBuilds++; return ForeignNativeQuote(quoteId, number); }
        var firstRequest = await safe.Store.StoreRequestAsync(requestEnvelope, Hash('b'), Request);
        var replayRequest = await safe.Store.StoreRequestAsync(requestEnvelope, Hash('b'), Request);
        var firstQuote = await safe.Store.StoreQuoteAsync(quoteEnvelope, Hash('c'), Quote);
        var replayQuote = await safe.Store.StoreQuoteAsync(quoteEnvelope, Hash('c'), Quote);
        Assert.Equal(requestId, firstRequest.Request.Id); Assert.Equal(requestId, replayRequest.Request.Id);
        Assert.Equal(quoteId, firstQuote.Quote.Id); Assert.Equal(quoteId, replayQuote.Quote.Id);
        Assert.Equal(1, requestBuilds); Assert.Equal(1, quoteBuilds);
        Assert.Equal(firstRequest.OriginalCorrelationId, replayRequest.OriginalCorrelationId);
        Assert.Equal(firstQuote.OriginalCorrelationId, replayQuote.OriginalCorrelationId);
        Assert.Equal(before with { Requests = before.Requests + 1, Quotes = before.Quotes + 1,
            RequestAttempts = before.RequestAttempts + 1, QuoteAttempts = before.QuoteAttempts + 1, Audits = before.Audits + 2 }, await ForeignNativeCounts(run));
        AssertReturnClean(safe);
    }

    private static Task<SalesCustomerReturnCommandResult> RunReturnWithForeignCallback(Run run, ReturnSafetyScope safe, Guid id,
        string key, Func<CancellationToken, Task> fence)
    {
        var body = new SalesDraftStateCommandRequest("2"); var metadata = Metadata(key);
        var envelope = new SalesDraftWriteEnvelope(Approver, SalesPermissionManifestV1.ReturnsApprove, [], metadata.CorrelationId,
            metadata.CausationId, key, "Web", null, null);
        // Real native core over the same actual store, enclosed in the production owned boundary.
        // The injected callback is an admission canary, not a replacement authorization implementation.
        var native = run.Service(safe.Store, CompanyA, Approver, null);
        return safe.Store.RunCustomerReturnDecisionAsync(envelope, SalesCustomerReturnHash.Compute(SalesCustomerReturnCommands.ApproveV1, id, body),
            SalesCustomerReturnCommands.ApproveV1, true, ct => native.ApproveCustomerReturnAsync(id, body, metadata, ct),
            _ => Task.FromResult<SalesCustomerReturnCommandResult?>(null), fence, default);
    }

    private static async Task AssertForeignCreatesBlocked(Run run, ReturnSafetyScope safe, ForeignMutationCommandProbe probe, CancellationToken ct)
    {
        var commands = probe.Commands; var builds = 0;
        SalesRequest Request(string number) { builds++; return ForeignNativeRequest(Guid.NewGuid(), number); }
        SalesQuote Quote(string number) { builds++; return ForeignNativeQuote(Guid.NewGuid(), number); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.StoreRequestAsync(
            run.Envelope("foreign-request-" + Guid.NewGuid().ToString("N"), Approver, SalesPermissionManifestV1.RequestsWrite), Hash('b'), Request, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => safe.Store.StoreQuoteAsync(
            run.Envelope("foreign-quote-" + Guid.NewGuid().ToString("N"), Approver, SalesPermissionManifestV1.QuotesWrite), Hash('c'), Quote, ct));
        Assert.Equal(0, builds); Assert.Equal(commands, probe.Commands);
        Assert.False(safe.Db.ChangeTracker.HasChanges()); Assert.False(safe.Writer.HasPendingDrafts);
    }

    private static SalesRequest ForeignNativeRequest(Guid id, string number)
    {
        var (unit, product) = ForeignNativeSnapshots();
        return SalesRequest.Create(id, CompanyA, number, new(Guid.NewGuid(), Guid.NewGuid(), Day),
            [new(Guid.NewGuid(), product, unit, "1.25", 1.25m)], Approver, DateTimeOffset.UtcNow);
    }
    private static SalesQuote ForeignNativeQuote(Guid id, string number)
    {
        var (unit, product) = ForeignNativeSnapshots(); var currency = Guid.NewGuid();
        var money = new MoneyProfileSnapshot(CompanyA, currency, SalesOrderDraft.MoneyOperation, Guid.NewGuid(), "3", Day, true, Day.AddYears(-1), null, 2);
        return SalesQuote.Create(id, CompanyA, number, new(Guid.NewGuid(), Guid.NewGuid(), currency, Day), money, [], new(),
            [new(Guid.NewGuid(), product, unit, "1.25", 1.25m)], Approver, DateTimeOffset.UtcNow);
    }
    private static (UnitProfileSnapshot Unit, ProductSnapshot Product) ForeignNativeSnapshots()
    {
        var unitId = Guid.NewGuid(); var profile = Guid.NewGuid();
        var unit = new UnitProfileSnapshot(CompanyA, unitId, "kg", "weight", profile, "4", true, 3, 3, true, Day.AddYears(-1), null);
        var binding = new UnitBindingSnapshot(CompanyA, unitId, profile, "4");
        return (unit, new ProductSnapshot(Guid.NewGuid(), "7", "SENTETIK-FOREIGN", "Sentetik bant", Day, binding, binding, binding));
    }
    private sealed record ForeignNativeEffect(int Requests, int Quotes, int RequestAttempts, int QuoteAttempts, int Audits);
    private static async Task<ForeignNativeEffect> ForeignNativeCounts(Run run) => new(
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Requests]"), await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Quotes]"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[RequestAttempts]"), await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[QuoteAttempts]"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] IN (N'Sales.Request',N'Sales.Quote')"));
    private sealed class ForeignMutationCommandProbe : DbCommandInterceptor
    {
        internal int Commands;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) { Commands++; return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) { Commands++; return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<object> result, CancellationToken token = default) { Commands++; return ValueTask.FromResult(result); }
    }
}
