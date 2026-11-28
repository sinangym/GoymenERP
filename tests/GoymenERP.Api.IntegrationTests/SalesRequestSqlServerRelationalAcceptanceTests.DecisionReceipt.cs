using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesRequestSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Request_original_decision_receipt_uses_actual_immutable_Audit_while_native_replay_still_returns_current_row()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run);
        await using var safe = ApprovalScope(run);
        var approved = await safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata("receipt-approve", "SENTETIK-receipt-original"));
        var proof = Assert.IsType<SalesRequestOriginalDecisionReceiptV1>(approved.OriginalDecisionReceipt);
        Assert.Equal((CompanyA, id, run.Actor, "1", "2", "Approved", "SENTETIK-receipt-original"),
            (proof.CompanyId, proof.RequestId, proof.ActorId, proof.ExpectedVersion, proof.ResultVersion, proof.State, proof.OriginalCorrelationId));
        Assert.Equal(SalesRequestDecisionDigestsV1.OriginalDecisionDigest(proof), proof.OriginalDecisionDigest);
        Assert.Equal(SalesRequestDecisionDigestsV1.CurrentResponseDigest(approved), proof.CurrentResponseDigest);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE Id=@id AND IntegrityHash=@hash", ("@id", proof.AuditEventId), ("@hash", proof.AuditIntegrityHash)));
        var rejected = await safe.Service.RejectRequestAsync(id, new("2", "  SENTETIK ret  "), run.Metadata("receipt-reject"));
        var rejectProof = Assert.IsType<SalesRequestOriginalDecisionReceiptV1>(rejected.OriginalDecisionReceipt);
        Assert.Equal(("2", "3", "Rejected", "  SENTETIK ret  "), (rejectProof.ExpectedVersion, rejectProof.ResultVersion, rejectProof.State, rejectProof.Decision.RejectionReason));
        var replay = await safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata("receipt-approve", "SENTETIK-current-retry"));
        var original = Assert.IsType<SalesRequestOriginalDecisionReceiptV1>(replay.OriginalDecisionReceipt);
        Assert.Equal((true, "Rejected", "3", "SENTETIK-receipt-original"), (replay.IsReplay, replay.Detail.Request.State, replay.Detail.Request.Version, replay.OriginalCorrelationId));
        Assert.Equal(proof with { CurrentResponseDigest = original.CurrentResponseDigest }, original);
        Assert.NotEqual(proof.CurrentResponseDigest, original.CurrentResponseDigest);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(replay, SalesDraftJson.Options));
        Assert.Equal(3, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("originalDecisionReceipt", out _));
        Assert.Equal(2, await DecisionCount(run, id)); Assert.Equal(2, await DecisionAuditCount(run, id));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.RequestAttempts WHERE RequestId=@id AND ResultJson IS NOT NULL", ("@id", id)));
    }

    [SqlServerFact]
    public async Task Selected_request_read_is_price_free_full_company_scoped_and_preserves_Approved_rejection_action()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run); await using var safe = ApprovalScope(run);
        foreach (var state in new[] { "Submitted", "Approved", "Rejected" })
        {
            if (state == "Approved") await safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata("human-approve"));
            if (state == "Rejected") await safe.Service.RejectRequestAsync(id, new("2", "SENTETIK ret"), run.Metadata("human-reject"));
            var callbacks = 0;
            var read = await safe.Service.ReadRequestDecisionAsync(id, _ => { callbacks++; return Task.CompletedTask; });
            Assert.Equal(SalesRequestDecisionReadStateV1.Found, read.State); var preview = Assert.IsType<SalesRequestDecisionDetailV1>(read.Detail);
            Assert.Equal((state, state == "Submitted", state != "Rejected"), (preview.Detail.Request.State, preview.ApprovalSupported, preview.RejectionSupported));
            Assert.Equal(preview.Detail.Request.LineCount, preview.Detail.Lines.Count); Assert.NotEmpty(preview.Detail.Lines);
            Assert.Equal(SalesRequestDecisionDigestsV1.SourceFactsDigest(preview.Detail), preview.SourceFactsDigest);
            Assert.Equal(SalesRequestDecisionDigestsV1.PreviewDigest(preview), preview.PreviewDigest); Assert.Equal(4, callbacks);
        }
        await using var foreign = run.Store(CompanyB);
        var foreignReads = 0;
        Assert.Null(await ((ISalesRequestDecisionReadStoreV1)foreign.Requests).ReadRequestDecisionAsync(id, _ => { foreignReads++; return Task.CompletedTask; }));
        Assert.Equal(2, foreignReads);
        Assert.DoesNotContain(safe.Db.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        Assert.Null(safe.Db.Database.CurrentTransaction); Assert.False(safe.Writer.HasPendingDrafts);
        // These synthetic owner fixtures grant only RequestsApprove; no general/price read is added by this port.
    }

    [SqlServerFact]
    public async Task Selected_request_read_consumers_cannot_release_private_facts_after_normal_completion_with_revocation_or_cancel()
    {
        await using var run = await Run.StartAsync(); var id = await SeedRequest(run);
        foreach (var boundary in new[] { 1, 2, 3, 4 }) foreach (var cancel in new[] { false, true })
        {
            await using var safe = ApprovalScope(run); using var cancelled = new CancellationTokenSource(); var callbacks = 0;
            Task Consumer(CancellationToken _)
            {
                if (++callbacks == boundary) { if (cancel) cancelled.Cancel(); else safe.Authority.Granted = false; }
                return Task.CompletedTask;
            }
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ReadRequestDecisionAsync(id, Consumer, cancelled.Token));
            else
            {
                var result = await safe.Service.ReadRequestDecisionAsync(id, Consumer);
                Assert.Equal(SalesRequestDecisionReadStateV1.Denied, result.State); Assert.Null(result.Detail);
            }
            Assert.Equal(boundary, callbacks); Assert.Empty(safe.Db.ChangeTracker.Entries()); Assert.False(safe.Writer.HasPendingDrafts);
        }
        Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
    }

    [SqlServerFact]
    public async Task Request_receipt_attempt_and_Audit_read_boundaries_revoke_or_cancel_before_native_commit_then_same_key_decides_once()
    {
        await using var run = await Run.StartAsync();
        foreach (var boundary in new[] { 2, 3, 4, 5 }) foreach (var cancel in new[] { false, true })
        {
            var id = await SeedRequest(run); var key = $"receipt-fence-{id:N}"; var save = new RequestSaveProbe();
            await using (var safe = ApprovalScope(run, save))
            {
                using var cancelled = new CancellationTokenSource(); var savedFences = 0;
                safe.Authority.AfterAwait = () =>
                {
                    if (save.Saved && ++savedFences == boundary)
                    { if (cancel) cancelled.Cancel(); else safe.Authority.Granted = false; }
                };
                // post-save1, attempt-before2/after3, Audit-before4/after5: every new actual SQL await is fenced.
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata(key), cancelled.Token));
                else Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => safe.Service.ApproveRequestAsync(id, new("1"), run.Metadata(key)))).Status);
                Assert.True(save.Saved); Assert.Equal(1, save.Saves); Assert.Empty(safe.Db.ChangeTracker.Entries());
                Assert.False(safe.Writer.HasPendingDrafts); Assert.Null(safe.Db.Database.CurrentTransaction);
            }
            Assert.Equal(0, await DecisionCount(run, id)); Assert.Equal(0, await DecisionAuditCount(run, id));
            await using var retry = ApprovalScope(run);
            Assert.False((await retry.Service.ApproveRequestAsync(id, new("1"), run.Metadata(key))).IsReplay);
            var replay = await retry.Service.ApproveRequestAsync(id, new("1"), run.Metadata(key, "later"));
            Assert.True(replay.IsReplay); Assert.NotNull(replay.OriginalDecisionReceipt);
            Assert.Equal(1, await DecisionCount(run, id)); Assert.Equal(1, await DecisionAuditCount(run, id));
        }
    }
}
