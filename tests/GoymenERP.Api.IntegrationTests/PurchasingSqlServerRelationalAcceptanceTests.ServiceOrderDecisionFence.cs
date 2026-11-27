using System.Data;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Service_order_native_post_save_fence_rolls_back_approval_revision_attempt_and_actual_audit_then_retries()
    {
        await using var run = await Run.StartAsync();
        var draft = ServiceDraft(run);
        await using (var create = ServiceScope(run, CompanyA))
            await create.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("service-fence-create"),
                Hash("service-fence-create"), "SENTETIK kayıt", (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
        var before = await ServiceDecisionCountsAsync(run, draft.OrderId);
        var allowed = true;
        var execution = new WorkDecisionExecutionFenceV1();
        using var lease = execution.Enter(_ => Task.FromResult(allowed));
        var authority = new WorkDecisionAuthorityV1(new RequestDecisionAuthority(), execution);
        var auditAppends = 0;
        // Synthetic eligibility revocation after the real Audit append; this is native store atomicity,
        // not a real Work-delegation database or complete application/master-owner acceptance.
        await using (var writer = ServiceScope(run, CompanyA, afterAudit: () => { auditAppends++; allowed = false; }))
        {
            async Task Fence(CancellationToken token)
            {
                var scope = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersApprove, CompanyA, token);
                if (!scope.IsGranted) throw new PurchasingServiceException(403, "forbidden");
            }
            var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => writer.Store.WriteAsync(draft.OrderId,
                PurchaseServiceOrderCommandsV1.Approve, run.Envelope("service-fence-approve", actor: OrderApprover), Hash("service-fence-approve"),
                "SENTETIK onay", (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1, OrderApprover, DateTimeOffset.UtcNow)), Fence, default));
            Assert.Equal(403, denied.Status);
            Assert.Equal(1, auditAppends);
            Assert.Empty(writer.Database.ChangeTracker.Entries());
        }
        Assert.Equal(before, await ServiceDecisionCountsAsync(run, draft.OrderId));
        allowed = true;
        await using (var retry = ServiceScope(run, CompanyA))
        {
            var accepted = await retry.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve,
                run.Envelope("service-fence-approve", actor: OrderApprover), Hash("service-fence-approve"), "SENTETIK onay",
                (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1, OrderApprover, DateTimeOffset.UtcNow)), _ => Task.CompletedTask, default);
            Assert.Equal(2L, accepted!.Order!.Version);
        }
        Assert.Equal((before.Version + 1, before.Revisions + 1, before.Attempts + 1, before.Audits + 1),
            await ServiceDecisionCountsAsync(run, draft.OrderId));
    }

    [SqlServerFact]
    public async Task Service_order_native_replay_rechecks_current_fence_without_another_revision_receipt_or_audit()
    {
        await using var run = await Run.StartAsync();
        var draft = ServiceDraft(run);
        var envelope = run.Envelope("service-fence-replay", actor: OrderApprover);
        await using (var create = ServiceScope(run, CompanyA))
        {
            await create.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Create, run.Envelope("service-fence-create"),
                Hash("service-fence-create"), "SENTETIK kayıt", (_, _) => Task.FromResult(draft), _ => Task.CompletedTask, default);
            await create.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve, envelope, Hash("service-fence-replay"),
                "SENTETIK onay", (old, _) => Task.FromResult(PurchaseServiceOrder.Approve(old!, 1, OrderApprover, DateTimeOffset.UtcNow)),
                _ => Task.CompletedTask, default);
        }
        var before = await ServiceDecisionCountsAsync(run, draft.OrderId);
        await using var replay = ServiceScope(run, CompanyA);
        var checks = 0;
        var answer = await replay.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve,
            envelope with { CorrelationId = "SENTETIK-later" }, Hash("service-fence-replay"), "SENTETIK onay",
            (_, _) => throw new InvalidOperationException("Replay cannot mutate"), _ => { checks++; return Task.CompletedTask; }, default);
        Assert.True(answer!.IsReplay);
        Assert.Equal(envelope.CorrelationId, answer.OriginalCorrelationId);
        Assert.Equal(2, checks);
        checks = 0;
        await Assert.ThrowsAsync<PurchasingServiceException>(() => replay.Store.WriteAsync(draft.OrderId, PurchaseServiceOrderCommandsV1.Approve,
            envelope, Hash("service-fence-replay"), "SENTETIK onay", (_, _) => throw new InvalidOperationException("Replay cannot mutate"),
            _ => ++checks == 2 ? throw new PurchasingServiceException(403, "forbidden") : Task.CompletedTask, default));
        Assert.Empty(replay.Database.ChangeTracker.Entries());
        Assert.Equal(before, await ServiceDecisionCountsAsync(run, draft.OrderId));
    }

    private static async Task<(long Version, int Revisions, int Attempts, int Audits)> ServiceDecisionCountsAsync(Run run, Guid id) =>
        (await run.ScalarAsync<long>("SELECT Version FROM purchasing.ServiceOrders WHERE OrderId=@id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderRevisions WHERE OrderId=@id", ("@id", id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.ServiceOrderAttempts"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Purchasing.ServiceOrder' AND EntityId=@id", ("@id", id.ToString("D"))));

    private sealed class ServiceDecisionAuditWriter(IAuditTransactionWriterV1 inner, Action afterAppend) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default)
        { await inner.WritePendingAsync(transaction, token); afterAppend(); }
        public void DiscardPending() => inner.DiscardPending();
    }
}
