using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Theory]
    [InlineData("restored")]
    [InlineData("prestage")]
    [InlineData("postsave")]
    public async Task Native_order_approval_refuses_permission_loss_before_staging_and_before_commit(string phase)
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("native-order-create"));
        var id = created.Detail.Order.Id;
        var before = (await world.Store.FindOrderAsync(id))!.Order;
        var snapshot = (before.Status, before.Version, before.Approval, before.ApprovalVersion, before.UpdatedAtUtc);
        var audits = world.Store.Audits.Count; var envelopes = world.Store.Envelopes.Count;
        Action revoke = () => world.Authority.Granted = false;
        if (phase == "restored") world.Store.BeforeMutation = revoke;
        if (phase == "prestage") world.Store.BeforeOrderStage = revoke;
        if (phase == "postsave") world.Store.AfterOrderSave = revoke;
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() =>
            world.Service().ApproveOrderAsync(id, new("2"), Metadata("native-order-approve"))));
        Assert.Equal(403, error.Status);
        var unchanged = (await world.Store.FindOrderAsync(id))!.Order;
        Assert.Equal(snapshot, (unchanged.Status, unchanged.Version, unchanged.Approval, unchanged.ApprovalVersion, unchanged.UpdatedAtUtc));
        Assert.Empty(world.Store.Transitions); Assert.Empty(world.Store.Refusals);
        Assert.Equal(audits, world.Store.Audits.Count); Assert.Equal(envelopes, world.Store.Envelopes.Count);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task Native_order_approval_refuses_actor_or_company_drift_after_the_synthetic_save_boundary(string drift)
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("native-actor-create"));
        world.Store.AfterOrderSave = () =>
        {
            if (drift == "actor") world.User.CurrentActor = Actor;
            else world.Company.CurrentCompanyId = Guid.NewGuid();
        };
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() =>
            world.Service().ApproveOrderAsync(created.Detail.Order.Id, new("2"), Metadata("native-actor-approve"))));
        Assert.Equal(403, error.Status);
        Assert.Equal(PurchaseOrderStatus.Draft, (await world.Store.FindOrderAsync(created.Detail.Order.Id))!.Order.Status);
        Assert.Empty(world.Store.Transitions); Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task Native_order_approval_same_key_retries_after_late_refusal_and_replay_does_not_mutate_twice()
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("native-retry-create"));
        var id = created.Detail.Order.Id;
        world.Store.AfterOrderSave = () => world.Authority.Granted = false;
        await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() => world.Service().ApproveOrderAsync(id, new("2"), Metadata("native-retry"))));
        world.Authority.Granted = true; world.Store.AfterOrderSave = null;
        var approved = await world.AsApprover(() => world.Service().ApproveOrderAsync(id, new("2"), Metadata("native-retry")));
        var replay = await world.AsApprover(() => world.Service().ApproveOrderAsync(id, new("2"), Metadata("native-retry", "SENTETIK-later")));
        Assert.Equal("3", approved.Detail.Order.Version); Assert.True(replay.IsReplay);
        Assert.Equal(approved.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Single(world.Store.Transitions);
    }

    private sealed partial class FakeStore
    {
        public Action? BeforeOrderStage, AfterOrderSave;
        public Task<PurchasingOrderStoreResult?> MutateOrderGuardedAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
            Guid orderId, Action<PurchaseOrder, DateTimeOffset> mutate, PurchasingMutationAudit audit,
            Func<CancellationToken, Task> freshDecisionFence, CancellationToken cancellationToken = default) =>
            MutateOrderFakeCoreAsync(envelope, requestHash, commandName, orderId, mutate, audit, cancellationToken, freshDecisionFence);
    }
}
