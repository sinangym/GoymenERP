using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesOrderOverDeliveryApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Over_request_owner_selected_native_decision_binds_actual_audit_receipt_and_foreign_company_reads_nothing()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var approve in new[] { true, false })
        {
            var id = await SeedOwnedOverRequest(run); await using var safe = OverRequestScope(run);
            var requestId = await run.ScalarAsync<Guid>("SELECT Id FROM sales.ApprovalRequests WHERE CompanyId=@company AND SubjectId=@id AND RequestNo=1", ("@company", CompanyA), ("@id", id));
            var read = await safe.Service.ReadOverDeliveryRequestDecisionAsync(requestId, _ => Task.CompletedTask);
            Assert.Equal(SalesOverDeliveryRequestDecisionReadStateV1.Found, read.State); var detail = Assert.IsType<SalesOverDeliveryRequestDecisionDetailV1>(read.Detail);
            Assert.Equal(("1", 4L, "100", "102.5"), (detail.Request.Version, detail.Order.Version, detail.Request.Ordered, detail.Request.RequestedTotal));
            var proof = SalesOverDeliveryRequestDecisionBindingV1.Proof(detail); var target = SalesOverDeliveryRequestDecisionBindingV1.Target(proof, "1");
            var result = approve ? await safe.Service.ApproveSelectedOverDeliveryRequestAsync(target, new("1"), OverRequestMetadata("over-selected-" + id.ToString("N")))
                : await safe.Service.RejectSelectedOverDeliveryRequestAsync(target, new("1", "  SENTETIK ret  "), OverRequestMetadata("over-selected-" + id.ToString("N")));
            var receipt = Assert.IsType<SalesOverDeliveryRequestOriginalReceiptV1>(result.OriginalOverDeliveryReceipt);
            Assert.Equal((1L, 2L), (receipt.ExpectedRequestVersion, receipt.ResultRequestVersion));
            Assert.Equal(approve ? (long?)4 : null, receipt.OriginalOrderVersionBefore); Assert.Equal(approve ? (long?)5 : null, receipt.OriginalOrderVersionAfter);
            Assert.True(SalesOverDeliveryRequestDecisionBindingV1.MatchesDecisionItem(SalesOverDeliveryRequestDecisionBindingV1.Item(detail), result, receipt));
            using var wire = JsonDocument.Parse(JsonSerializer.Serialize(result, SalesDraftJson.Options));
            Assert.Equal(3, wire.RootElement.EnumerateObject().Count()); Assert.False(wire.RootElement.TryGetProperty("originalOverDeliveryReceipt", out _));
            var companyB = new CompanyContext(); companyB.Set(CompanyB);
            await using var dbB = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString).Options, companyB);
            var storeB = new SalesStore(dbB, companyB, new AuditEventCollector(), TimeProvider.System);
            Assert.Null(await storeB.ReadOverDeliveryRequestDecisionSourceAsync(requestId, _ => Task.CompletedTask));
            Assert.Empty(dbB.ChangeTracker.Entries()); AssertOverRequestClean(safe);
        }
    }

    [SqlServerFact]
    public async Task Over_request_original_key_and_work_selected_version_survive_a_later_genuine_order_approval_without_remapping_seen_total()
    {
        await using var run = await Run.StartAsync(owner: null); var id = await SeedOwnedOverRequest(run); var key = "over-retained-" + id.ToString("N");
        SalesOverDeliveryRequestOriginalReceiptV1 original; Guid requestId;
        await using (var first = OverRequestScope(run))
        {
            var answer = await OverRequestDecide(first, run, id, key, true); original = Assert.IsType<SalesOverDeliveryRequestOriginalReceiptV1>(answer.OriginalOverDeliveryReceipt);
            requestId = original.Target.RequestId; Assert.Equal(((long?)4, (long?)5), (original.OriginalOrderVersionBefore, original.OriginalOrderVersionAfter));
        }
        // A real second native request covers a larger total. Its approval advances the order again, independently of request1.
        Assert.Equal((SalesOverDeliveryGateResultV1.Held, (int?)2), ((await GateAsync(run, id, original.Target.LineId, 100m, 2.6m)) is var gate ? (gate.Result, gate.RequestNo) : default));
        await using (var second = OverRequestScope(run))
            Assert.Equal("Approved", (await second.Service.ApproveOverDeliveryRequestAsync(id, 2, new("1"), OverRequestMetadata("over-later-" + id.ToString("N")))).Request.Status);
        Assert.Equal(6L, await OverRequestVersion(run, id));
        await using var reopened = OverRequestScope(run);
        var historical = await reopened.Service.ReadOverDeliveryRequestOriginalVersionAsync(requestId, "1", _ => Task.CompletedTask);
        Assert.Equal(SalesOverDeliveryRequestDecisionReadStateV1.Found, historical.State); var retained = Assert.IsType<SalesOverDeliveryRequestOriginalReceiptV1>(historical.Receipt);
        Assert.Equal(original.OriginalDecisionDigest, retained.OriginalDecisionDigest); Assert.Equal(((long?)4, (long?)5), (retained.OriginalOrderVersionBefore, retained.OriginalOrderVersionAfter));
        Assert.Equal("102.5", retained.OriginalRequest.RequestedTotal);
        var company = new CompanyContext(); company.Set(CompanyA);
        var source = new SalesOverDeliveryRequestWorkInboxSourceV1(new SalesWorkInboxApprovalStoreV1(reopened.Db), company, reopened.User, reopened.Authority, reopened.Service);
        var snapshot = await source.ReadDecisionVersionAsync(new(CompanyA, reopened.User.Actor, "SENTETIK", null), requestId, "1", default); Assert.NotNull(snapshot);
        Assert.False(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion); Assert.Equal(2L, snapshot.SalesOverDeliveryRequestProof!.RequestVersion);
        var replay = await OverRequestDecide(reopened, run, id, key, true);
        Assert.True(replay.IsReplay); Assert.Equal(original.OriginalDecisionDigest, replay.OriginalOverDeliveryReceipt!.OriginalDecisionDigest);
        Assert.True(SalesOverDeliveryRequestDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot, replay, new("1"), key, reopened.User.Actor, true));
        Assert.Equal((2, 2, 6L), (await OverRequestAttemptCount(run, id), await OverRequestApprovalCount(run, id), await OverRequestVersion(run, id)));
        AssertOverRequestClean(reopened);
    }

    [SqlServerFact]
    public async Task Over_request_last_actual_receipt_read_normal_completion_cancel_or_revoke_aborts_before_commit_and_same_key_retries_once()
    {
        await using var run = await Run.StartAsync(owner: null);
        foreach (var cancel in new[] { true, false })
        {
            var id = await SeedOwnedOverRequest(run); var key = "over-final-owner-read-" + id.ToString("N"); var probe = new OverRequestFinalReceiptProbe(); var save = new OverRequestSaveProbe();
            using var cancellation = new CancellationTokenSource();
            await using (var safe = OverRequestScope(run, probe, save))
            {
                probe.AfterLastRead = () => { Assert.True(save.Saved); if (cancel) cancellation.Cancel(); else safe.Authority.Granted = false; };
                Task<SalesApprovalRequestCommandResult> Execute() => safe.Service.ApproveOverDeliveryRequestAsync(id, 1, new("1"), OverRequestMetadata(key), cancellation.Token);
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Execute);
                else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(Execute)).Status);
                Assert.True(probe.Fired); Assert.Equal(2, probe.AuditReads); AssertOverRequestClean(safe);
            }
            Assert.Equal((0, 0, 0, 4L), (await OverRequestAttemptCount(run, id), await OverRequestAuditCount(run, id), await OverRequestApprovalCount(run, id), await OverRequestVersion(run, id)));
            await using var retry = OverRequestScope(run); var answer = await OverRequestDecide(retry, run, id, key, true);
            Assert.NotNull(answer.OriginalOverDeliveryReceipt); Assert.Equal((1, 1), (await OverRequestAttemptCount(run, id), await OverRequestApprovalCount(run, id)));
        }
    }

    [SqlServerFact]
    public async Task Over_request_selected_request_id_mismatch_never_mutates_and_historical_actor_does_not_borrow_another_receipt()
    {
        await using var run = await Run.StartAsync(owner: null); var id = await SeedOwnedOverRequest(run);
        await using var safe = OverRequestScope(run); var requestId = await run.ScalarAsync<Guid>("SELECT Id FROM sales.ApprovalRequests WHERE CompanyId=@company AND SubjectId=@id", ("@company", CompanyA), ("@id", id));
        var current = (await safe.Service.ReadOverDeliveryRequestDecisionAsync(requestId, _ => Task.CompletedTask)).Detail!;
        var target = SalesOverDeliveryRequestDecisionBindingV1.Target(SalesOverDeliveryRequestDecisionBindingV1.Proof(current), "1");
        Assert.Equal(503, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveSelectedOverDeliveryRequestAsync(target with { RequestId = Guid.NewGuid() }, new("1"), OverRequestMetadata("over-wrong-target")))).Status);
        Assert.Equal((0, 0), (await OverRequestAttemptCount(run, id), await OverRequestApprovalCount(run, id))); AssertOverRequestClean(safe);
        await OverRequestDecide(safe, run, id, "over-original-actor", true); safe.User.Actor = "SENTETIK-different-checker";
        var other = await safe.Service.ReadOverDeliveryRequestOriginalVersionAsync(requestId, "1", _ => Task.CompletedTask);
        Assert.Equal(SalesOverDeliveryRequestDecisionReadStateV1.NotFound, other.State); Assert.Null(other.Receipt); Assert.Equal(2L, other.CurrentVersion);
        var body = new SalesDraftStateCommandRequest("1");
        Assert.Equal(409, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveOverDeliveryRequestAsync(id, 1, body, OverRequestMetadata("over-original-actor")))).Status);
        Assert.Equal((1, 1), (await OverRequestAttemptCount(run, id), await OverRequestApprovalCount(run, id)));
    }

    private sealed class OverRequestFinalReceiptProbe : DbCommandInterceptor
    {
        internal int AuditReads; internal bool Fired; internal Action? AfterLastRead;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("AuditEvents", StringComparison.Ordinal) && ++AuditReads == 2) { Fired = true; AfterLastRead?.Invoke(); }
            return ValueTask.FromResult(result);
        }
    }
}
