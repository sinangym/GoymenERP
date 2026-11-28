using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesShipmentRiskExceptionSqlServerRelationalAcceptanceTests
{
    // Actual existing SQL owner/attempt/audit fixture. Controlled authority callbacks are not identity or simultaneous-race acceptance.
    [SqlServerFact]
    public async Task Risk_selected_original_receipt_is_genuine_while_current_business_day_and_source_version_remain_separate()
    {
        await using var run = await Run.StartAsync();
        foreach (var approve in new[] { true, false })
        {
            var order = await SeedOwnedRisk(run); var key = $"risk-receipt-{order:N}"; await using var safe = RiskDecisionScope(run);
            var first = await RiskDecide(safe, order, key, approve); var receipt = first.OriginalDecisionReceipt; Assert.NotNull(receipt);
            Assert.Equal(("1", "2", safe.User.Actor, first.OriginalCorrelationId), (receipt.ExpectedVersion, receipt.ResultVersion, receipt.ActorId, receipt.OriginalCorrelationId));
            Assert.Equal(approve ? "Approved" : "Rejected", receipt.After.State); Assert.Equal(4L, receipt.CurrentSource!.OrderVersion);
            var laterClock = new Clock(T0.AddDays(1)); var company = run.Company(CompanyA);
            var store = new SalesStore(safe.Db, company, safe.Collector, laterClock, officialCancellationAuditWriter: safe.Writer);
            var service = new SalesRiskExceptionService(company, safe.User, safe.Authority, new RiskDecisionCorrelation(), store, store, laterClock).WithRiskExceptionDecisionSafety();
            var replay = approve ? await service.ApproveRiskExceptionAsync(order, 1, new("1"), RiskDecisionMetadata(key))
                : await service.RejectRiskExceptionAsync(order, 1, new("1", "  SENTETIK ret  "), RiskDecisionMetadata(key));
            Assert.True(replay.IsReplay); Assert.False(replay.Exception.IsValidToday); Assert.NotNull(replay.OriginalDecisionReceipt);
            Assert.Equal(receipt.OriginalDecisionDigest, replay.OriginalDecisionReceipt.OriginalDecisionDigest);
            Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
            var history = await service.ReadRiskExceptionOriginalVersionAsync(receipt.ExceptionId, "1", _ => Task.CompletedTask);
            Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Found, history.State); Assert.NotNull(history.Receipt);
            Assert.Equal(receipt.OriginalDecisionDigest, history.Receipt.OriginalDecisionDigest);
            var source = new SalesRiskExceptionWorkInboxSourceV1(new SalesWorkInboxStoreV1(safe.Db), company, safe.User, safe.Authority, service);
            var context = new WorkInboxSourceContextV1(CompanyA, safe.User.Actor, "SENTETIK-risk-selected", null);
            var snapshot = await source.ReadDecisionVersionAsync(context, receipt.ExceptionId, "1", default); Assert.NotNull(snapshot);
            Assert.False(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion); Assert.Equal("2", snapshot.SalesRiskExceptionProof!.CurrentVersion);
            Assert.True(SalesRiskExceptionDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot, replay,
                approve ? SalesRiskExceptionCommands.ApproveV1 : SalesRiskExceptionCommands.RejectV1, new("1", approve ? null : "  SENTETIK ret  "), key, safe.User.Actor));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(replay, SalesDraftJson.Options));
            Assert.Equal(3, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("originalDecisionReceipt", out _));
            Assert.Equal((1, 1, 2L, 4L), (await RiskAttemptCount(run, order), await RiskDecisionAuditCount(run, order), await RiskVersion(run, order), await RiskOrderVersion(run, order)));
            Assert.All(safe.Authority.Permissions, p => Assert.Equal(SalesPermissionManifestV1.OrdersRiskExceptionApprove, p)); AssertRiskClean(safe);
        }
    }
    [SqlServerFact]
    public async Task Risk_noncanonical_legacy_version_keeps_native_success_and_replay_without_forging_work_provenance()
    {
        await using var run = await Run.StartAsync(); var order = await SeedOwnedRisk(run); await using var safe = RiskDecisionScope(run);
        const string key = "risk-legacy-raw-version";
        var first = await safe.Service.ApproveRiskExceptionAsync(order, 1, new("01"), RiskDecisionMetadata(key));
        Assert.Equal("2", first.Exception.Version); Assert.False(first.IsReplay); Assert.Null(first.OriginalDecisionReceipt);
        var replay = await safe.Service.ApproveRiskExceptionAsync(order, 1, new("01"), RiskDecisionMetadata(key));
        Assert.True(replay.IsReplay); Assert.Equal(first.Exception, replay.Exception); Assert.Null(replay.OriginalDecisionReceipt);
        var id = await RiskExceptionId(run, order);
        var history = await safe.Service.ReadRiskExceptionOriginalVersionAsync(id, "1", _ => Task.CompletedTask);
        Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Unavailable, history.State); Assert.Null(history.Receipt);
        Assert.Equal((1, 1, 2L), (await RiskAttemptCount(run, order), await RiskDecisionAuditCount(run, order), await RiskVersion(run, order))); AssertRiskClean(safe);
    }
    [SqlServerFact]
    public async Task Risk_selected_read_final_completed_permission_loss_or_cancel_releases_no_facts_and_preserves_caller_audit()
    {
        await using var run = await Run.StartAsync(); var order = await SeedOwnedRisk(run); var id = await RiskExceptionId(run, order);
        foreach (var cancelFinal in new[] { false, true })
        {
            await using var safe = RiskDecisionScope(run); safe.Collector.Enqueue(RiskDecisionCallerAudit()); using var cancellation = new CancellationTokenSource(); var consumers = 0;
            // Owner first1, selected query pre/post2..3, outer return4, final release5.
            Task<SalesRiskExceptionDecisionReadResultV1> Read() => safe.Service.ReadRiskExceptionDecisionAsync(id, _ =>
            {
                if (++consumers == 5) { if (cancelFinal) cancellation.Cancel(); else safe.Authority.Granted = false; }
                return Task.CompletedTask;
            }, cancellation.Token);
            if (cancelFinal) await Assert.ThrowsAnyAsync<OperationCanceledException>(Read);
            else { var denied = await Read(); Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Denied, denied.State); Assert.Null(denied.Detail); }
            Assert.Equal(5, consumers); Assert.True(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            Assert.Equal((0, 0, 1L), (await RiskAttemptCount(run, order), await RiskDecisionAuditCount(run, order), await RiskVersion(run, order)));
        }
        await using var maker = RiskDecisionScope(run); maker.User.Actor = Shipper;
        var own = await maker.Service.ReadRiskExceptionDecisionAsync(id, _ => Task.CompletedTask);
        Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Denied, own.State); Assert.Null(own.Detail);
        var absent = await maker.Service.ReadRiskExceptionDecisionAsync(Guid.NewGuid(), _ => Task.CompletedTask);
        Assert.Equal(SalesRiskExceptionDecisionReadStateV1.NotFound, absent.State); Assert.Null(absent.Detail);
    }
    [SqlServerFact]
    public async Task Risk_receipt_await_boundaries_revoke_or_cancel_before_commit_then_same_key_records_exactly_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var phase in new[] { 3, 4, 7 }) foreach (var cancelFinal in new[] { false, true }) foreach (var approve in new[] { false, true })
        {
            var order = await SeedOwnedRisk(run); var key = $"risk-receipt-fence-{order:N}"; var save = new RiskDecisionSaveProbe(); using var cancellation = new CancellationTokenSource();
            await using (var safe = RiskDecisionScope(run, save))
            {
                var savedFences = 0; var fired = false;
                safe.Authority.AfterAwait = () =>
                {
                    // Save post1; receipt before2/afterAttempt3/afterAudit4; source pre5/post6; return7.
                    if (save.Saved && ++savedFences == phase) { fired = true; if (cancelFinal) cancellation.Cancel(); else safe.Authority.Granted = false; }
                    return Task.CompletedTask;
                };
                Task<SalesRiskExceptionCommandResult> Decide() => approve
                    ? safe.Service.ApproveRiskExceptionAsync(order, 1, new("1"), RiskDecisionMetadata(key), cancellation.Token)
                    : safe.Service.RejectRiskExceptionAsync(order, 1, new("1", "  SENTETIK ret  "), RiskDecisionMetadata(key), cancellation.Token);
                if (cancelFinal) await Assert.ThrowsAnyAsync<OperationCanceledException>(Decide);
                else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(Decide)).Status);
                Assert.True(fired); Assert.True(save.Saved); AssertRiskClean(safe);
            }
            Assert.Equal((0, 0, 1L, 4L), (await RiskAttemptCount(run, order), await RiskDecisionAuditCount(run, order), await RiskVersion(run, order), await RiskOrderVersion(run, order)));
            await using var retry = RiskDecisionScope(run); var first = await RiskDecide(retry, order, key, approve);
            Assert.False(first.IsReplay); Assert.NotNull(first.OriginalDecisionReceipt);
            var replay = await RiskDecide(retry, order, key, approve); Assert.True(replay.IsReplay); Assert.NotNull(replay.OriginalDecisionReceipt);
            Assert.Equal(first.OriginalDecisionReceipt.OriginalDecisionDigest, replay.OriginalDecisionReceipt.OriginalDecisionDigest);
            Assert.Equal((1, 1, 2L), (await RiskAttemptCount(run, order), await RiskDecisionAuditCount(run, order), await RiskVersion(run, order))); AssertRiskClean(retry);
        }
    }
    [SqlServerFact]
    public async Task Risk_selected_and_historical_read_markers_refuse_other_owned_reentry_without_mutation_or_caller_cleanup()
    {
        await using var run = await Run.StartAsync();
        foreach (var historical in new[] { false, true })
        {
            var order = await SeedOwnedRisk(run); await using var safe = RiskDecisionScope(run);
            if (historical) await RiskDecide(safe, order, $"risk-reentry-original-{order:N}", true);
            var id = await RiskExceptionId(run, order); var company = run.Company(CompanyA);
            var store = new SalesStore(safe.Db, company, safe.Collector, new Clock(T0), officialCancellationAuditWriter: safe.Writer);
            Assert.True(store.QuoteApprovalEntryIsClean); var callbacks = 0; var commands = 0;
            var envelope = new SalesDraftWriteEnvelope(safe.User.Actor, SalesPermissionManifestV1.QuotesExpiredConversionApprove, [],
                "SENTETIK-risk-reentrant", null, $"risk-reentrant-quote-{order:N}", "test");
            async Task Probe(CancellationToken token)
            {
                callbacks++;
                // Root composes the reciprocal risk-read exclusion in the existing owner guards.
                Assert.False(store.QuoteApprovalEntryIsClean); Assert.False(store.RequestApprovalEntryIsClean);
                Assert.False(store.OverDeliveryRequestApprovalEntryIsClean); Assert.False(store.DiscountApprovalEntryIsClean);
                Assert.False(store.PriceFloorApprovalEntryIsClean); Assert.False(store.RiskExceptionDecisionEntryIsClean);
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadRiskExceptionDecisionSourceAsync(id, _ => throw new InvalidOperationException("unreachable nested consumer"), token));
                await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadRiskExceptionOriginalVersionAsync(id, 1, safe.User.Actor,
                    _ => throw new InvalidOperationException("unreachable nested history consumer"), token));
                await Assert.ThrowsAsync<SalesDraftStoreException>(() => store.RunQuoteApprovalAsync<SalesQuoteResponse>(envelope, new string('a', 64),
                    SalesQuoteCommands.ApproveExpiredConversionV1, true, _ => { commands++; throw new InvalidOperationException("unreachable mutation"); },
                    _ => Task.FromResult<SalesQuoteResponse?>(null), r => r.IsReplay, _ => Task.CompletedTask, token));
                Assert.Null(safe.Db.Database.CurrentTransaction); Assert.False(safe.Writer.HasPendingDrafts);
            }
            if (historical)
            {
                var result = await store.ReadRiskExceptionOriginalVersionAsync(id, 1, safe.User.Actor, Probe, default);
                Assert.Equal(SalesRiskExceptionDecisionReadStateV1.Found, result.State); Assert.NotNull(result.Receipt);
                Assert.Equal(6, callbacks); // Before/after candidates, source pre/post, source return, after exact audit.
            }
            else { Assert.NotNull(await store.ReadRiskExceptionDecisionSourceAsync(id, Probe)); Assert.Equal(2, callbacks); }
            Assert.Equal(0, commands); Assert.True(store.QuoteApprovalEntryIsClean); AssertRiskClean(safe);
            Assert.Equal((historical ? 1 : 0, historical ? 1 : 0, historical ? 2L : 1L),
                (await RiskAttemptCount(run, order), await RiskDecisionAuditCount(run, order), await RiskVersion(run, order)));

            // Caller work is read, never adopted, cleared or discarded by a selected read.
            var caller = new ShipmentRiskExceptionAttemptRow { CompanyId = CompanyA, OrderId = order, CommandName = SalesRiskExceptionCommands.RequestV1,
                IdempotencyKeyHash = new string('F', 64), RequestHash = new string('c', 64), ActorId = safe.User.Actor,
                Outcome = 1, ExceptionId = id, CorrelationId = "SENTETIK-caller", RecordedAtUtc = T0 };
            safe.Db.ShipmentRiskExceptionAttempts.Add(caller); safe.Collector.Enqueue(RiskDecisionCallerAudit());
            Assert.NotNull(await store.ReadRiskExceptionDecisionSourceAsync(id, _ => Task.CompletedTask));
            Assert.Equal(Microsoft.EntityFrameworkCore.EntityState.Added, safe.Db.Entry(caller).State);
            Assert.True(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            Assert.Equal(historical ? 1 : 0, await RiskAttemptCount(run, order));
        }
        // The opposite direction also refuses before the risk consumer/SQL read, preserving the other owner's TX.
        await using var owned = RiskDecisionScope(run); var ownedCompany = run.Company(CompanyA);
        var ownedStore = new SalesStore(owned.Db, ownedCompany, owned.Collector, new Clock(T0), officialCancellationAuditWriter: owned.Writer);
        var ownedEnvelope = new SalesDraftWriteEnvelope(owned.User.Actor, SalesPermissionManifestV1.QuotesExpiredConversionApprove, [],
            "SENTETIK-owned-quote", null, "risk-read-inside-quote", "test");
        var riskConsumers = 0; var executed = 0;
        var refusal = await Assert.ThrowsAsync<SalesDraftStoreException>(() => ownedStore.RunQuoteApprovalAsync<SalesQuoteResponse>(ownedEnvelope, new string('b', 64),
            SalesQuoteCommands.ApproveExpiredConversionV1, true, async token =>
            {
                executed++; var transaction = owned.Db.Database.CurrentTransaction; Assert.NotNull(transaction);
                Task Consumer(CancellationToken _) { riskConsumers++; return Task.CompletedTask; }
                await Assert.ThrowsAsync<InvalidOperationException>(() => ownedStore.ReadRiskExceptionDecisionSourceAsync(Guid.NewGuid(), Consumer, token));
                await Assert.ThrowsAsync<InvalidOperationException>(() => ownedStore.ReadRiskExceptionOriginalVersionAsync(Guid.NewGuid(), 1, owned.User.Actor, Consumer, token));
                Assert.Same(transaction, owned.Db.Database.CurrentTransaction); Assert.Equal(0, riskConsumers);
                throw new InvalidOperationException("SENTETIK abort only the owned quote fixture");
            }, _ => Task.FromResult<SalesQuoteResponse?>(null), r => r.IsReplay, _ => Task.CompletedTask, default));
        Assert.Equal(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, refusal.Code); Assert.Equal(1, executed); Assert.Equal(0, riskConsumers); AssertRiskClean(owned);
        var quoteReadCallbacks = 0;
        Assert.Null(await ownedStore.ReadQuoteConversionDecisionAsync(Guid.NewGuid(), async token =>
        {
            quoteReadCallbacks++;
            await Assert.ThrowsAsync<InvalidOperationException>(() => ownedStore.ReadRiskExceptionDecisionSourceAsync(Guid.NewGuid(),
                _ => { riskConsumers++; return Task.CompletedTask; }, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => ownedStore.ReadRiskExceptionOriginalVersionAsync(Guid.NewGuid(), 1, owned.User.Actor,
                _ => { riskConsumers++; return Task.CompletedTask; }, token));
        }));
        Assert.Equal(2, quoteReadCallbacks); Assert.Equal(0, riskConsumers); AssertRiskClean(owned);
    }
    private static Task<Guid> RiskExceptionId(Run run, Guid order) => run.ScalarAsync<Guid>("SELECT Id FROM sales.ShipmentRiskExceptions WHERE CompanyId=@company AND OrderId=@id AND ExceptionNo=1", ("@company", CompanyA), ("@id", order));
}
