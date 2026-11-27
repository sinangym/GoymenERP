using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Legacy_supplier_return_store_without_rejection_boundary_fails_closed_without_a_save()
    {
        var (world, order, returns) = await ReturnWorldAsync(); var request = ReturnRequest();
        await world.Service(returns: returns).CreateSupplierReturnAsync(order, request, Metadata("legacy-return-create"));
        var service = world.Service(returns: new LegacyReturnStore(returns));
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() => service
            .RejectSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", "SENTETIK ret"), Metadata("legacy-return-reject"))));
        Assert.Equal((503, PurchaseSupplierReturnCodesV1.Unavailable), (error.Status, error.Code)); Assert.Equal(0, returns.DecisionWrites);
    }

    [Theory]
    [InlineData("read")] [InlineData("save")] [InlineData("actor")] [InlineData("company")]
    public async Task Supplier_return_rejection_aborts_on_fresh_authority_or_context_loss(string phase)
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var service = world.Service(returns: returns); var request = ReturnRequest();
        await service.CreateSupplierReturnAsync(order, request, Metadata("return-fence-create"));
        Action lose = phase == "actor" ? () => world.User.CurrentActor = Actor
            : phase == "company" ? () => world.Company.CurrentCompanyId = Guid.NewGuid() : () => world.Authority.Granted = false;
        if (phase == "read") returns.AfterAccess = lose; else returns.AfterDecisionSave = lose;
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() => service
            .RejectSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", "SENTETIK ret"), Metadata("return-fence"))));
        Assert.Equal(403, error.Status); Assert.Equal(0, returns.DecisionWrites);
        Assert.Equal("PendingCostException", returns.Stored!.Return.Status.ToString()); Assert.Equal(1, returns.Stored.Version);
    }

    [Fact]
    public async Task Supplier_return_rejection_same_key_retries_after_abort_then_replays_original_correlation()
    {
        var (world, order, returns) = await ReturnWorldAsync(); var service = world.Service(returns: returns); var request = ReturnRequest();
        await service.CreateSupplierReturnAsync(order, request, Metadata("return-retry-create"));
        returns.AfterDecisionSave = () => world.Authority.Granted = false;
        var body = new DecidePurchaseSupplierReturnV1("1", "SENTETIK ret");
        await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() => service.RejectSupplierReturnCostExceptionAsync(
            order, request.ReturnId, body, Metadata("return-retry", "SENTETIK-original"))));
        world.Authority.Granted = true; returns.AfterDecisionSave = null;
        var saved = await world.AsApprover(() => service.RejectSupplierReturnCostExceptionAsync(order, request.ReturnId, body, Metadata("return-retry", "SENTETIK-original")));
        var replay = await world.AsApprover(() => service.RejectSupplierReturnCostExceptionAsync(order, request.ReturnId, body, Metadata("return-retry", "SENTETIK-later")));
        Assert.Equal(("Rejected", "2", false), (saved.Return.Status, saved.Return.Version, saved.IsReplay));
        Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-original", replay.OriginalCorrelationId); Assert.Equal(1, returns.DecisionWrites);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Supplier_return_rejection_preserves_durable_maker_or_version_refusal(bool maker)
    {
        var (world, order, returns) = await ReturnWorldAsync(); var service = world.Service(returns: returns); var request = ReturnRequest();
        await service.CreateSupplierReturnAsync(order, request, Metadata("return-terminal-create"));
        if (!maker) world.User.CurrentActor = Approver;
        var body = new DecidePurchaseSupplierReturnV1(maker ? "1" : "99", "SENTETIK ret");
        var first = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RejectSupplierReturnCostExceptionAsync(order, request.ReturnId, body, Metadata("return-terminal", "SENTETIK-original")));
        var replay = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RejectSupplierReturnCostExceptionAsync(order, request.ReturnId, body, Metadata("return-terminal", "SENTETIK-later")));
        Assert.Equal(409, first.Status); Assert.Equal(first.Code, replay.Code); Assert.False(first.IsReplay); Assert.True(replay.IsReplay);
        Assert.Equal("SENTETIK-original", replay.OriginalCorrelationId); Assert.Equal(0, returns.DecisionWrites);
    }

    private sealed partial class ReturnStoreFake
    {
        public Action? AfterDecisionSave;
        public async Task<T> ExecuteRejectionGuardedAsync<T>(Func<CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task> freshDecisionFence, CancellationToken cancellationToken = default)
        {
            var old = Stored; var receipts = attempts.ToArray(); var writes = DecisionWrites;
            try { await freshDecisionFence(cancellationToken); var result = await operation(cancellationToken);
                await freshDecisionFence(cancellationToken); return result; }
            catch { Stored = old; DecisionWrites = writes; attempts.Clear(); foreach (var receipt in receipts) attempts.Add(receipt.Key, receipt.Value); throw; }
        }
    }

    private sealed class LegacyReturnStore(ReturnStoreFake inner) : IPurchaseSupplierReturnStore
    {
        public Task<PurchaseSupplierReturnReplay?> FindReplayAsync(PurchasingWriteEnvelope e, string h, string c, CancellationToken t = default) => inner.FindReplayAsync(e, h, c, t);
        public Task<StoredPurchaseSupplierReturn?> FindAsync(Guid o, Guid r, CancellationToken t = default) => inner.FindAsync(o, r, t);
        public Task<bool> ReturnIdExistsAsync(Guid r, CancellationToken t = default) => inner.ReturnIdExistsAsync(r, t);
        public Task<IReadOnlyList<StoredPurchaseSupplierReturn>> ListForOrderAsync(Guid o, int n, CancellationToken t = default) => inner.ListForOrderAsync(o, n, t);
        public Task<Guid?> FindAcceptanceLineAsync(Guid o, Guid a, CancellationToken t = default) => inner.FindAcceptanceLineAsync(o, a, t);
        public Task<StoredPurchaseSupplierReturn> SaveNewAsync(PurchaseSupplierReturnDraft d, PurchasingWriteEnvelope e, string h, string c, CancellationToken t = default) => inner.SaveNewAsync(d, e, h, c, t);
        public Task<StoredPurchaseSupplierReturn> SaveDecisionAsync(Guid o, PurchaseSupplierReturn d, long v, PurchasingWriteEnvelope e, string h, string c, CancellationToken t = default) => inner.SaveDecisionAsync(o, d, v, e, h, c, t);
        public Task RecordRefusalAsync(PurchasingWriteEnvelope e, string h, string c, PurchasingTerminalRefusal r, CancellationToken t = default) => inner.RecordRefusalAsync(e, h, c, r, t);
    }
}
