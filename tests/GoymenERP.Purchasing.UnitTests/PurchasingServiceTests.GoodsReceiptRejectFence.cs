using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("save")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task Native_receipt_rejection_aborts_the_synthetic_attempt_when_authority_or_context_changes(string phase)
    {
        var (world, runner, orderId, batchId, store) = await RejectionWorldAsync();
        Action loseAuthority = () => world.Authority.Granted = false;
        if (phase == "read") store.BeforeDecisionRead = loseAuthority;
        else store.AfterDecisionSave = phase == "actor" ? () => world.User.CurrentActor = Actor
            : phase == "company" ? () => world.Company.CurrentCompanyId = Guid.NewGuid() : loseAuthority;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() =>
            world.Service().RejectGoodsReceiptBatchAsync(orderId, batchId, new("1", "SENTETIK ret"), Metadata("batch-fence"))));
        Assert.Equal(403, denied.Status);
        Assert.Equal((0, 0), (store.DecisionWrites, store.DecisionAttempts));
        store.BeforeDecisionRead = null;
        Assert.Equal(PurchaseGoodsReceiptBatchStatus.PendingApproval, (await store.FindAsync(orderId, batchId))!.Batch.Status);
        Assert.Empty(runner.Purchases); Assert.Empty(runner.Stocks);
    }

    [Fact]
    public async Task Native_receipt_rejection_retries_after_abort_and_replay_preserves_original_correlation_without_posting_stock()
    {
        var (world, runner, orderId, batchId, store) = await RejectionWorldAsync();
        store.AfterDecisionSave = () => world.Authority.Granted = false;
        var request = new DecidePurchaseGoodsReceiptBatchV1("1", "SENTETIK ret");
        await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() => world.Service()
            .RejectGoodsReceiptBatchAsync(orderId, batchId, request, Metadata("batch-retry", "SENTETIK-original"))));
        world.Authority.Granted = true; store.AfterDecisionSave = null;
        var accepted = await world.AsApprover(() => world.Service().RejectGoodsReceiptBatchAsync(orderId, batchId,
            request, Metadata("batch-retry", "SENTETIK-original")));
        var replay = await world.AsApprover(() => world.Service().RejectGoodsReceiptBatchAsync(orderId, batchId,
            request, Metadata("batch-retry", "SENTETIK-later")));
        Assert.Equal(("Rejected", "2", false), (accepted.Batch.Status, accepted.Batch.Version, accepted.IsReplay));
        Assert.True(replay.IsReplay); Assert.Equal("SENTETIK-original", replay.OriginalCorrelationId);
        Assert.Equal((1, 1), (store.DecisionWrites, store.DecisionAttempts));
        store.BeforeDecisionRead = () => world.Authority.Granted = false;
        await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() => world.Service()
            .RejectGoodsReceiptBatchAsync(orderId, batchId, request, Metadata("batch-retry"))));
        Assert.Equal((1, 1), (store.DecisionWrites, store.DecisionAttempts));
        Assert.Empty(runner.Purchases); Assert.Empty(runner.Stocks);
    }

    [Theory]
    [InlineData("maker")]
    [InlineData("version")]
    public async Task Native_receipt_rejection_keeps_its_terminal_refusal_receipt_and_replays_it(string reason)
    {
        var (world, runner, orderId, batchId, store) = await RejectionWorldAsync();
        var request = new DecidePurchaseGoodsReceiptBatchV1(reason == "version" ? "99" : "1", "SENTETIK ret");
        if (reason != "maker") world.User.CurrentActor = Approver;
        var first = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service()
            .RejectGoodsReceiptBatchAsync(orderId, batchId, request, Metadata("batch-terminal", "SENTETIK-original")));
        var replay = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service()
            .RejectGoodsReceiptBatchAsync(orderId, batchId, request, Metadata("batch-terminal", "SENTETIK-later")));
        Assert.Equal(409, first.Status); Assert.Equal(first.Code, replay.Code);
        Assert.False(first.IsReplay); Assert.True(replay.IsReplay);
        Assert.Equal("SENTETIK-original", replay.OriginalCorrelationId);
        Assert.Equal((0, 1), (store.DecisionWrites, store.DecisionAttempts));
        Assert.Empty(runner.Stocks);
    }

    [Fact]
    public async Task Legacy_batch_store_without_a_guarded_boundary_does_not_fall_back_to_its_existing_save()
    {
        var (world, runner, orderId, batchId, store) = await RejectionWorldAsync();
        world.BatchStore = new LegacyBatchStore(store);
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.AsApprover(() => world.Service()
            .RejectGoodsReceiptBatchAsync(orderId, batchId, new("1", "SENTETIK ret"), Metadata("batch-legacy"))));
        Assert.Equal((503, PurchaseGoodsReceiptBatchCodesV1.Unavailable), (denied.Status, denied.Code));
        Assert.Equal((0, 0), (store.DecisionWrites, store.DecisionAttempts));
        Assert.Empty(runner.Stocks);
    }

    private sealed class LegacyBatchStore(FakeBatchStore inner) : IPurchaseGoodsReceiptBatchStore
    {
        public Task<PurchaseGoodsReceiptBatchReplay?> FindReplayAsync(PurchasingWriteEnvelope e, string h, string c, CancellationToken t = default) => inner.FindReplayAsync(e, h, c, t);
        public Task<StoredPurchaseGoodsReceiptBatch?> FindAsync(Guid o, Guid b, CancellationToken t = default) => inner.FindAsync(o, b, t);
        public Task<bool> BatchIdExistsAsync(Guid b, CancellationToken t = default) => inner.BatchIdExistsAsync(b, t);
        public Task<IReadOnlyList<StoredPurchaseGoodsReceiptBatch>> ListForOrderAsync(Guid o, int n, CancellationToken t = default) => inner.ListForOrderAsync(o, n, t);
        public Task<IReadOnlySet<string>> TakenLabelCodesAsync(IReadOnlyCollection<string> c, CancellationToken t = default) => inner.TakenLabelCodesAsync(c, t);
        public Task<IReadOnlySet<string>> TakenLabelCodesStartingWithAsync(string p, int n, CancellationToken t = default) => inner.TakenLabelCodesStartingWithAsync(p, n, t);
        public Task<long> NextGeneratedNumberAsync(CancellationToken t = default) => inner.NextGeneratedNumberAsync(t);
        public Task<IReadOnlyList<PurchaseGoodsReceiptLabelMatchV1>> SearchLabelsAsync(string s, int n, CancellationToken t = default) => inner.SearchLabelsAsync(s, n, t);
        public Task<StoredPurchaseGoodsReceiptBatch> SaveNewAsync(PurchaseGoodsReceiptBatchDraft d, PurchasingWriteEnvelope e, string h, string c, CancellationToken t = default) => inner.SaveNewAsync(d, e, h, c, t);
        public Task<StoredPurchaseGoodsReceiptBatch> SaveDecisionAsync(Guid o, PurchaseGoodsReceiptBatch d, long v, PurchasingWriteEnvelope e, string h, string c, CancellationToken t = default) => inner.SaveDecisionAsync(o, d, v, e, h, c, t);
        public Task RecordRefusalAsync(PurchasingWriteEnvelope e, string h, string c, PurchasingTerminalRefusal r, CancellationToken t = default) => inner.RecordRefusalAsync(e, h, c, r, t);
    }

    private static async Task<(World World, BatchRunner Runner, Guid Order, Guid Batch, FakeBatchStore Store)> RejectionWorldAsync()
    {
        var (world, runner, order, line, version, productVersion) = await BatchWorldAsync(false, 1m);
        var pending = await world.Service().SubmitGoodsReceiptBatchAsync(order, line,
            BatchOf(version, productVersion, false, BatchLabel("SENTETIK-fence", "1", null)), Metadata("batch-fence-submit"));
        Assert.Equal("PendingApproval", pending.Batch.Status);
        return (world, runner, order, pending.Batch.BatchId, (FakeBatchStore)world.BatchStore!);
    }

    private sealed partial class FakeBatchStore
    {
        private readonly Dictionary<(string Command, string Key), (string Hash, PurchaseGoodsReceiptBatchReplay Result)> decisionReceipts = [];
        public Action? BeforeDecisionRead, AfterDecisionSave;
        public int DecisionWrites, DecisionAttempts;
        public async Task<T> ExecuteRejectionGuardedAsync<T>(Func<CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task> freshDecisionFence, CancellationToken cancellationToken = default)
        {
            var oldBatches = batches.ToArray(); var oldReceipts = decisionReceipts.ToArray();
            var oldWrites = DecisionWrites; var oldAttempts = DecisionAttempts;
            try { await freshDecisionFence(cancellationToken); var result = await operation(cancellationToken);
                await freshDecisionFence(cancellationToken); return result; }
            catch { batches.Clear(); foreach (var item in oldBatches) batches.Add(item.Key, item.Value);
                decisionReceipts.Clear(); foreach (var item in oldReceipts) decisionReceipts.Add(item.Key, item.Value);
                DecisionWrites = oldWrites; DecisionAttempts = oldAttempts; throw; }
        }
        private PurchaseGoodsReceiptBatchReplay? ReadBatchDecisionReplay(PurchasingWriteEnvelope envelope, string hash, string command)
        {
            if (!decisionReceipts.TryGetValue((command, envelope.IdempotencyKey), out var receipt)) return null;
            if (receipt.Hash != hash) throw new PurchasingStoreException(PurchasingStoreCodes.SameKeyPayloadMismatch, "SENTETIK mismatch");
            return receipt.Result;
        }
        private StoredPurchaseGoodsReceiptBatch SaveBatchDecision(PurchaseGoodsReceiptBatch decided, long expected,
            PurchasingWriteEnvelope envelope, string hash, string command)
        {
            var old = batches[decided.Id]; Assert.Equal(expected, old.Version);
            var next = old with { Batch = old.Batch with { Status = decided.Status }, Version = decided.Version,
                DecidedBy = decided.DecidedBy, DecisionReason = decided.Reason };
            batches[decided.Id] = next; DecisionWrites++; DecisionAttempts++;
            decisionReceipts.Add((command, envelope.IdempotencyKey), (hash, new(decided.Id, null, envelope.CorrelationId)));
            AfterDecisionSave?.Invoke(); return next;
        }
        private void RecordBatchDecisionRefusal(PurchasingWriteEnvelope envelope, string hash, string command, PurchasingTerminalRefusal refusal)
        { decisionReceipts.Add((command, envelope.IdempotencyKey), (hash, new(null, refusal, envelope.CorrelationId))); DecisionAttempts++; }
    }
}
