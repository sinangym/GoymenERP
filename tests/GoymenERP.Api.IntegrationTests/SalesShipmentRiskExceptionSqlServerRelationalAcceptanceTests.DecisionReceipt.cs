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
    private static Task<Guid> RiskExceptionId(Run run, Guid order) => run.ScalarAsync<Guid>("SELECT Id FROM sales.ShipmentRiskExceptions WHERE CompanyId=@company AND OrderId=@id AND ExceptionNo=1", ("@company", CompanyA), ("@id", order));
}
