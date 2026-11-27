using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AK-3b (ALC-S16, KLN-D119, BS-16/BS-17, golden TA4) through the service: a bulk receipt on a piece line takes a whole piece count
/// and the weighed kg on every label, under the single receipt's rules and problem codes. Each label is accepted in pieces and
/// carries its weighed kg; the theoretical/weighed comparison is a warning only, so a piece batch always posts. A kilogram line
/// keeps KLN-D43/KLN-D45 unchanged. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static async Task<(World World, BatchRunner Runner, Guid Order, Guid Line, string OrderVersion, string ProductVersion)>
        BatchWorldAsync(bool pieceLine, decimal? tolerance)
    {
        var (world, _, orderId, lineId) = pieceLine ? await PieceReceiptWorldAsync(tolerance) : await ReceiptWorldAsync();
        if (!pieceLine) world.ReceiptPolicy = new FixedReceiptPolicy(new PurchaseGoodsReceiptPolicyV1(tolerance, null, null));
        var runner = new BatchRunner();
        world.BatchStore = new FakeBatchStore();
        world.BatchTransactions = runner;
        var detail = await world.Service().GetOrderDetailAsync(orderId);
        return (world, runner, orderId, lineId, detail.Order.Version, detail.Lines.Single(line => line.LineId == lineId).ProductVersion);
    }

    private static PurchaseGoodsReceiptBatchLabelInputV1 BatchLabel(string supplierNo, string kg, string? pieces) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), supplierNo, null, kg, null, null, pieces);

    private static SubmitPurchaseGoodsReceiptBatchV1 BatchOf(string orderVersion, string productVersion, bool acknowledged,
        params PurchaseGoodsReceiptBatchLabelInputV1[] labels) =>
        new(orderVersion, productVersion, Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(2026, 11, 22, 8, 0, 0, TimeSpan.Zero), acknowledged, labels);

    [Fact]
    public async Task A_piece_bulk_receipt_posts_every_label_in_pieces_with_its_weighed_kg_and_only_warns_about_the_kg_difference()
    {
        // Golden K02/K03 as two labels: 40 pieces / 500,160 kg and 60 pieces / 748,900 kg; theoretical 100 x 12,345 = 1 234,500 kg,
        // weighed 1 249,060 kg -> +14,560 kg, outside the 1 % tolerance: a warning, never an approval.
        var (world, runner, orderId, lineId, orderVersion, productVersion) = await BatchWorldAsync(pieceLine: true, tolerance: 1m);
        var request = BatchOf(orderVersion, productVersion, acknowledged: false,
            BatchLabel("SENTETIK-TED-1", "500.16", "40"), BatchLabel("SENTETIK-TED-2", "748.9", "60"));

        var result = await world.Service().SubmitGoodsReceiptBatchAsync(orderId, lineId, request, Metadata("piece-batch"));

        Assert.Equal(nameof(PurchaseGoodsReceiptBatchStatus.Posted), result.Batch.Status);
        Assert.Equal(new PurchaseGoodsReceiptPieceKilogramsV1(100, "12.345", "1234.5", "1249.06", "14.56",
            nameof(GoodsReceiptKgVarianceOutcome.OutsideTolerance), "1"), result.Batch.PieceKilograms);
        Assert.Equal(("1234.5", "1249.06", "14.56"), (result.Batch.ExpectedQuantityKg, result.Batch.ReceivedQuantityKg, result.Batch.DifferenceKg));
        Assert.Equal([40, 60], result.Batch.Labels.Select(label => label.PieceCount));
        // Each label: accepted in pieces, weighed kg on the acceptance, piece count + kg to the stock.
        Assert.Equal([("40", "500.16"), ("60", "748.9")], runner.Purchases.Select(stage => (stage.AcceptedQuantity, stage.MeasuredQuantityKg!)));
        Assert.Equal([(40, "500.16"), (60, "748.9")], runner.Stocks.Select(stock => (stock.PieceCount!.Value, stock.QuantityKg)));
        Assert.True(runner.Committed);
    }

    [Theory]
    [InlineData(null)] // no Ayarlar tolerance: ToleranceNotSet still posts
    [InlineData("5")] // inside the tolerance: no acknowledgement is asked on a piece line
    public async Task A_piece_bulk_receipt_never_waits_for_an_approver_or_an_acknowledgement(string? tolerance)
    {
        var (world, runner, orderId, lineId, orderVersion, productVersion) = await BatchWorldAsync(pieceLine: true,
            tolerance: tolerance is null ? null : decimal.Parse(tolerance, System.Globalization.CultureInfo.InvariantCulture));

        var result = await world.Service().SubmitGoodsReceiptBatchAsync(orderId, lineId,
            BatchOf(orderVersion, productVersion, acknowledged: false, BatchLabel("SENTETIK-TED-1", "500.16", "40")), Metadata("piece-batch-posts"));

        Assert.Equal(nameof(PurchaseGoodsReceiptBatchStatus.Posted), result.Batch.Status);
        Assert.Equal(tolerance is null ? nameof(GoodsReceiptKgVarianceOutcome.ToleranceNotSet) : nameof(GoodsReceiptKgVarianceOutcome.WithinTolerance),
            result.Batch.PieceKilograms!.VarianceOutcome);
        Assert.Single(runner.Stocks);
    }

    [Theory]
    [InlineData(null)] // the piece count is missing
    [InlineData("2.5")] // a fraction of a piece
    public async Task A_piece_bulk_receipt_label_without_a_whole_piece_count_is_refused_with_the_single_receipts_code(string? pieces)
    {
        var (world, runner, orderId, lineId, orderVersion, productVersion) = await BatchWorldAsync(pieceLine: true, tolerance: 1m);
        var request = BatchOf(orderVersion, productVersion, acknowledged: false,
            BatchLabel("SENTETIK-TED-1", "500.16", "40"), BatchLabel("SENTETIK-TED-2", "30", pieces));

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SubmitGoodsReceiptBatchAsync(orderId, lineId, request, Metadata("piece-batch-fraction")));

        Assert.Equal((400, PurchaseGoodsReceiptCodesV1.PieceCountInvalid), (refusal.Status, refusal.Code));
        Assert.Equal("labels[1].acceptedQuantity", Assert.Single(refusal.Errors).Field);
        Assert.Empty(runner.Purchases);
    }

    [Fact]
    public async Task A_piece_bulk_receipt_never_exceeds_the_remaining_pieces()
    {
        var (world, runner, orderId, lineId, orderVersion, productVersion) = await BatchWorldAsync(pieceLine: true, tolerance: 1m);
        var request = BatchOf(orderVersion, productVersion, acknowledged: false,
            BatchLabel("SENTETIK-TED-1", "500.16", "40"), BatchLabel("SENTETIK-TED-2", "760", "61"));

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SubmitGoodsReceiptBatchAsync(orderId, lineId, request, Metadata("piece-batch-over")));

        Assert.Equal((409, PurchaseGoodsReceiptBatchCodesV1.ExceedsRemaining), (refusal.Status, refusal.Code));
        Assert.Empty(runner.Purchases);
    }

    [Fact]
    public async Task A_kilogram_bulk_receipt_takes_no_piece_count()
    {
        var (world, runner, orderId, lineId, orderVersion, productVersion) = await BatchWorldAsync(pieceLine: false, tolerance: 1m);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().SubmitGoodsReceiptBatchAsync(orderId, lineId,
            BatchOf(orderVersion, productVersion, acknowledged: true, BatchLabel("SENTETIK-TED-1", "1", "3")), Metadata("kg-batch-pieces")));

        Assert.Equal((400, PurchaseAcceptanceParticipantCodes.MeasuredKilogramsMismatch), (refusal.Status, refusal.Code));
        Assert.Equal("labels[0].acceptedQuantity", Assert.Single(refusal.Errors).Field);
        Assert.Empty(runner.Purchases);
    }

    [Fact]
    public async Task A_kilogram_bulk_receipt_keeps_its_own_rule_a_difference_outside_the_tolerance_waits_for_an_approver()
    {
        // KLN-D43/KLN-D45 unchanged: expected = the line's remaining kg; 1 kg of a larger remainder is far outside 1 %.
        var (world, runner, orderId, lineId, orderVersion, productVersion) = await BatchWorldAsync(pieceLine: false, tolerance: 1m);

        var result = await world.Service().SubmitGoodsReceiptBatchAsync(orderId, lineId,
            BatchOf(orderVersion, productVersion, acknowledged: false, BatchLabel("SENTETIK-TED-1", "1", null)), Metadata("kg-batch"));

        Assert.Equal(nameof(PurchaseGoodsReceiptBatchStatus.PendingApproval), result.Batch.Status);
        Assert.Null(result.Batch.PieceKilograms);
        Assert.Null(Assert.Single(result.Batch.Labels).PieceCount);
        Assert.Empty(runner.Purchases);
    }

    [Fact]
    public void A_bulk_receipt_label_piece_count_is_an_exact_positive_text_and_stays_out_of_a_kilogram_request_hash()
    {
        var kilogram = BatchOf("3", "SENTETIK-v1", false, BatchLabel("SENTETIK-TED-1", "250.5", null));
        PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(kilogram);
        PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(BatchOf("3", "SENTETIK-v1", false, BatchLabel("SENTETIK-TED-1", "250.5", "20")));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(
            BatchOf("3", "SENTETIK-v1", false, BatchLabel("SENTETIK-TED-1", "250.5", "0"))));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(
            BatchOf("3", "SENTETIK-v1", false, BatchLabel("SENTETIK-TED-1", "250.5", "-1"))));

        // A kilogram request serializes exactly as before AK-3b, so an earlier idempotency key still replays.
        Assert.DoesNotContain("acceptedQuantity", System.Text.Json.JsonSerializer.Serialize(kilogram, PurchasingJson.Options), StringComparison.Ordinal);
    }

    private sealed class BatchRunner : IPurchaseGoodsReceiptBatchTransactionV1, IPurchaseGoodsReceiptBatchParticipantsV1
    {
        public List<PurchaseAcceptanceStageRequestV1> Purchases { get; } = [];
        public List<PurchaseReceiptStockRequestV1> Stocks { get; } = [];
        public bool Committed { get; private set; }

        public async Task<T> ExecuteAsync<T>(Guid companyId, PurchaseGoodsReceiptTargetV1 target,
            Func<IPurchaseGoodsReceiptBatchParticipantsV1, CancellationToken, Task<T>> operation, Func<CancellationToken, Task> beforeCommit,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(Company, companyId);
            var result = await operation(this, cancellationToken);
            await beforeCommit(cancellationToken);
            Committed = true;
            return result;
        }

        public Task<IPurchaseGoodsReceiptParticipantsV1> EnlistLabelAsync(int labelIndex, CancellationToken cancellationToken) =>
            Task.FromResult<IPurchaseGoodsReceiptParticipantsV1>(new LabelParticipants(this));

        private sealed class LabelParticipants(BatchRunner owner) : IPurchaseGoodsReceiptParticipantsV1
        {
            public Task<PurchaseReceiptStockRequestV1?> ReadOriginalAsync(Guid acceptanceId, PurchasingWriteEnvelope envelope, CancellationToken cancellationToken) =>
                Task.FromResult<PurchaseReceiptStockRequestV1?>(null);

            public Task<PurchaseReceiptFactsV1> ResolveFirstAsync(PurchaseReceiptSourceV1 source, Guid locationId, CancellationToken cancellationToken) =>
                Task.FromResult(new PurchaseReceiptFactsV1(Company, source.ProductId, source.ProductVersion, locationId, "Company", Company, "Company", Company));

            public Task<PurchaseAcceptanceStageResultV1> StagePurchaseAsync(PurchaseAcceptanceStageRequestV1 request, PurchasingWriteEnvelope envelope,
                CancellationToken cancellationToken)
            {
                owner.Purchases.Add(request);
                var next = (long.Parse(request.ExpectedPurchaseOrderVersion, System.Globalization.CultureInfo.InvariantCulture) + 1)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Task.FromResult(new PurchaseAcceptanceStageResultV1(Company, request.AcceptanceId, request.PurchaseOrderId,
                    request.PurchaseOrderLineId, next, request.AcceptedQuantity, "0", false, "synthetic-correlation"));
            }

            public Task<PurchaseReceiptStockResultV1> StageInventoryAsync(PurchaseReceiptStockRequestV1 request, PurchasingWriteEnvelope envelope,
                CancellationToken cancellationToken)
            {
                owner.Stocks.Add(request);
                return Task.FromResult(new PurchaseReceiptStockResultV1(Company, request.AcceptanceId, request.PurchaseOrderId,
                    request.PurchaseOrderLineId, request.PurchaseOrderVersionAfter, request.ProductId, request.ExpectedProductVersion,
                    request.PurchaseUnitId, request.LotId, request.LabelId, "1", request.MovementId, request.QuantityKg, false, "synthetic-correlation"));
            }
        }
    }

    /// <summary>An in-memory bulk receipt store: no code is taken, nothing replays; it keeps what the service decided.</summary>
    private sealed partial class FakeBatchStore : IPurchaseGoodsReceiptBatchStore
    {
        private readonly Dictionary<Guid, StoredPurchaseGoodsReceiptBatch> batches = [];

        public Task<PurchaseGoodsReceiptBatchReplay?> FindReplayAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName,
            CancellationToken cancellationToken = default) => Task.FromResult(ReadBatchDecisionReplay(envelope, requestHash, commandName));

        public Task<StoredPurchaseGoodsReceiptBatch?> FindAsync(Guid orderId, Guid batchId, CancellationToken cancellationToken = default)
        { BeforeDecisionRead?.Invoke(); return Task.FromResult(batches.GetValueOrDefault(batchId)); }

        public Task<bool> BatchIdExistsAsync(Guid batchId, CancellationToken cancellationToken = default) => Task.FromResult(batches.ContainsKey(batchId));

        public Task<IReadOnlyList<StoredPurchaseGoodsReceiptBatch>> ListForOrderAsync(Guid orderId, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredPurchaseGoodsReceiptBatch>>([.. batches.Values.Where(batch => batch.Batch.PurchaseOrderId == orderId).Take(take)]);

        public Task<IReadOnlySet<string>> TakenLabelCodesAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<IReadOnlySet<string>> TakenLabelCodesStartingWithAsync(string prefix, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<long> NextGeneratedNumberAsync(CancellationToken cancellationToken = default) => Task.FromResult(1L);

        public Task<IReadOnlyList<PurchaseGoodsReceiptLabelMatchV1>> SearchLabelsAsync(string text, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PurchaseGoodsReceiptLabelMatchV1>>([]);

        public Task<StoredPurchaseGoodsReceiptBatch> SaveNewAsync(PurchaseGoodsReceiptBatchDraft draft, PurchasingWriteEnvelope envelope, string requestHash,
            string commandName, CancellationToken cancellationToken = default)
        {
            var stored = new StoredPurchaseGoodsReceiptBatch(Company, draft, 1, envelope.ActorId, DateTimeOffset.UnixEpoch, null, null, null);
            batches.Add(draft.BatchId, stored);
            return Task.FromResult(stored);
        }

        public Task<StoredPurchaseGoodsReceiptBatch> SaveDecisionAsync(Guid orderId, PurchaseGoodsReceiptBatch decided, long expectedVersion,
            PurchasingWriteEnvelope envelope, string requestHash, string commandName, CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveBatchDecision(decided, expectedVersion, envelope, requestHash, commandName));

        public Task RecordRefusalAsync(PurchasingWriteEnvelope envelope, string requestHash, string commandName, PurchasingTerminalRefusal refusal,
            CancellationToken cancellationToken = default)
        { RecordBatchDecisionRefusal(envelope, requestHash, commandName, refusal); return Task.CompletedTask; }
    }
}
