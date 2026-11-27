using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using H = GoymenERP.Api.IntegrationTests.WorkInventoryTransferLossDecisionHostTests;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    // Real owner requests/stock/receipts/audit and purpose queries. Authority is a controlled test grant,
    // not hosted Identity/browser acceptance; the shared native Run and its actual audit writer are reused.
    [SqlServerFact]
    public async Task Work_loss_approval_uses_native_owned_writeoff_and_exact_immutable_replay()
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-WORK-LOSS-APPROVE");
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        await using var scope = run.Store(CompanyA);
        var actor = LossChecker(run).ActorId;
        var host = LossPurposeHost(scope, actor);
        var id = InventoryWorkInboxFence.TransferLineItemId(WorkDecisionHostV1.InventoryTransferLoss, plan.TransferId, line);
        var preview = await host.PreviewAsync(WorkDecisionHostV1.InventoryTransferLoss, id, default);
        Assert.Equal("2", preview.SourceVersion); Assert.Equal(1, preview.InventoryTransferLoss!.RequestNumber);
        var body = new WorkDecisionRequestV1("2", true, LossRenewalReason, InventoryTransferLoss: new(1, At.AddHours(3)));
        var key = "work-loss-approve-" + Guid.NewGuid().ToString("N");
        var answer = await host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, id, body, key, default);
        Assert.Equal(("Approved", false), (answer.State, answer.Replayed));
        var replay = await host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, id, body, key, default);
        Assert.Equal(answer with { Replayed = true }, replay);
        await using var db = run.Context(CompanyA);
        var decision = Assert.Single(await db.TransferLossDecisions.Where(row => row.TransferId == plan.TransferId).ToListAsync());
        Assert.NotNull(decision.MovementId); Assert.Equal(actor, decision.DecidedBy); Assert.Equal(At.AddHours(3), decision.DecidedAtUtc);
        Assert.Single(await db.Commands.Where(row => row.CommandName == InventoryStore.DecideTransferLossCommand).ToListAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'inventory.transfer.loss-decided.v1' AND EntityId=@p", ("@p", plan.TransferId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Work_loss_same_version_renewal_keeps_old_key_on_old_request_and_never_remaps_a_fresh_stale_request()
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-WORK-LOSS-RENEW");
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        await using var scope = run.Store(CompanyA); var actor = LossChecker(run).ActorId;
        var host = LossPurposeHost(scope, actor);
        var id = InventoryWorkInboxFence.TransferLineItemId(WorkDecisionHostV1.InventoryTransferLoss, plan.TransferId, line);
        var body = new WorkDecisionRequestV1("2", false, LossRenewalReason, InventoryTransferLoss: new(1, At.AddHours(3)));
        var key = "work-loss-reject-" + Guid.NewGuid().ToString("N");
        var rejected = await host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, id, body, key, default);
        Assert.Equal(("Rejected", false), (rejected.State, rejected.Replayed));
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(4), LossRenewalExplanation), Metadata(run));
        var renewed = await host.PreviewAsync(WorkDecisionHostV1.InventoryTransferLoss, id, default);
        Assert.Equal((id, "2", 2), (renewed.ItemId, renewed.SourceVersion, renewed.InventoryTransferLoss!.RequestNumber));
        // Unknown retry retains the original request, timestamp and key even though the preview now names request 2.
        var replay = await host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, id, body, key, default);
        Assert.Equal(rejected with { Replayed = true }, replay);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(
            WorkDecisionHostV1.InventoryTransferLoss, id, body, "not-the-original-key", default))).Status);
        var nativeBody = new DecideInventoryTransferLossV1("2", InventoryTransferDecisionKindV1.Reject, At.AddHours(3), LossRenewalReason, 1);
        var original = await scope.Store.ReplayDecideLossAsync(plan.TransferId, line, nativeBody,
            Metadata(run) with { ActorId = actor, IdempotencyKey = key }, Authorized);
        Assert.NotNull(original); Assert.True(original.IsReplay); Assert.True(original.CommandReceiptProven);
        var domainPrior = await scope.Store.DecideLossAsync(plan.TransferId, line, nativeBody,
            Metadata(run) with { ActorId = actor, IdempotencyKey = "different-domain-prior-key" }, Authorized);
        Assert.True(domainPrior.IsReplay); Assert.False(domainPrior.CommandReceiptProven);
        await using (var db = run.Context(CompanyA))
        {
            Assert.Single(await db.TransferLossDecisions.Where(row => row.TransferId == plan.TransferId).ToListAsync());
            Assert.Equal(2L, (await db.Transfers.SingleAsync(row => row.Id == plan.TransferId)).Version);
        }
        var approved = await host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, id,
            new("2", true, LossRenewalReason, InventoryTransferLoss: new(2, At.AddHours(5))), "work-loss-renew-" + Guid.NewGuid().ToString("N"), default);
        Assert.Equal("Approved", approved.State);
        await using var final = run.Context(CompanyA);
        var decisions = await final.TransferLossDecisions.Where(row => row.TransferId == plan.TransferId).OrderBy(row => row.RequestNumber).ToListAsync();
        Assert.Equal([1, 2], decisions.Select(row => row.RequestNumber)); Assert.Null(decisions[0].MovementId); Assert.NotNull(decisions[1].MovementId);
        Assert.Equal(2, await final.Commands.CountAsync(row => row.CommandName == InventoryStore.DecideTransferLossCommand));
    }

    [SqlServerFact]
    public async Task Work_loss_delegation_revoked_after_native_SQL_save_rolls_back_stock_receipt_and_real_audit()
    {
        await using var run = await Run.StartAsync();
        var (plan, line) = await DispatchedLossTransferAsync(run, "SYN-WORK-LOSS-DELEGATION");
        await LossRequestAsync(run, plan.TransferId, line, new("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        var probe = new LossSafetySaveProbe(plan.TransferId);
        await using var scope = run.Store(CompanyA, probe);
        var delegated = new LossPurposeDelegation(() => !probe.Saved);
        var host = LossPurposeHost(scope, LossChecker(run).ActorId, delegated);
        var id = InventoryWorkInboxFence.TransferLineItemId(WorkDecisionHostV1.InventoryTransferLoss, plan.TransferId, line);
        var body = new WorkDecisionRequestV1("2", true, LossRenewalReason, Guid.NewGuid(), InventoryTransferLoss: new(1, At.AddHours(3)));
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.DecideAsync(
            WorkDecisionHostV1.InventoryTransferLoss, id, body, "work-loss-revoked", default))).Status);
        Assert.True(probe.Saved); Assert.Empty(scope.DbContext.ChangeTracker.Entries()); Assert.False(scope.AuditWriter.HasPendingDrafts);
        await AssertLossSafetyAbsent(run, plan.TransferId);
    }

    // Controlled delegation state only; the native store, SaveChanges interceptor, audit collector and rollback are real SQL.
    private sealed class LossPurposeDelegation(Func<bool> allowed) : IWorkDelegationReaderV1
    {
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkDelegationViewV1?>(allowed() ? new(id, CompanyA, item.Kind, InventoryPermissionManifestV1.StockTransferLossApprove,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(),
                DateTimeOffset.UtcNow, "SYNTHETIC delegated grant", null, null, null) : null);
    }

    private static WorkDecisionHostV1 LossPurposeHost(StoreScope scope, string actor, IWorkDelegationReaderV1? delegations = null)
    {
        var user = new H.LossUser(CompanyA, actor); var execution = new WorkDecisionExecutionFenceV1();
        var authority = new WorkDecisionAuthorityV1(new H.LossAuthority(), execution);
        var source = new InventoryTransferLossWorkInboxSourceV1(scope.DbContext,
            new InventoryWorkInboxFence(scope.CompanyContext, user, authority));
        // DecideLoss does not consult destination ownership; the native constructor still requires its existing port.
        var unusedDestination = System.Reflection.DispatchProxy.Create<IInventoryTransferDestinationPortV1, H.OwnerProxy>();
        var native = new InventoryTransferReceiptCommandService(scope.CompanyContext, user, authority, unusedDestination, scope.Store, scope.Store);
        return H.CreateHost(scope.CompanyContext, user, authority, source, native, execution, delegations);
    }
}
