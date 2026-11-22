using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AK-3 (KLN-D119, ALS-D119, BS-16/BS-17, golden TA4) through the service: a receipt on a piece line hands the whole accepted
/// pieces to Inventory as <c>PieceCount</c> and its weighed kilograms to the Purchasing acceptance, and answers how the weighed kg
/// compare with the theoretical kg (accepted × the kg/piece recorded at approval) against the Ayarlar tolerance — a warning only.
/// A kilogram line is unchanged (no piece count, no weighed kg on the acceptance). Golden R1, A04, A05. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static async Task<(World World, ReceiptRunner Runner, Guid Order, Guid Line)> PieceReceiptWorldAsync(decimal? tolerance)
    {
        var world = new World
        {
            Bindings = new(CreatePermission, ReadPermission, ApprovePermission, CreatePermission),
            TheoreticalKilograms = new FakeTheoreticalKilograms
            {
                Answer = PurchaseTheoreticalKilogramsPerUnitAnswerV1.Found(12.345m, "product.kg-per-unit.v1/sentetik"),
            },
            ReceiptPolicy = new FixedReceiptPolicy(new PurchaseGoodsReceiptPolicyV1(tolerance, null, null)),
        };
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "100", unitId: PieceUnitId), Metadata("piece-create"));
        await world.AsApprover(() => world.Service().ApproveOrderAsync(created.Detail.Order.Id, new("2"), Metadata("piece-approve")));
        return (world, new ReceiptRunner(), created.Detail.Order.Id, created.Detail.Lines[0].LineId);
    }

    private static RecordPurchaseGoodsReceiptV1 PieceReceipt(string pieces, string measuredKg) => ReceiptRequest() with
    {
        AcceptedQuantity = pieces,
        MeasuredQuantityKg = measuredKg,
    };

    [Fact]
    public async Task A_piece_receipt_sends_the_piece_count_and_weighed_kg_and_warns_outside_the_tolerance_without_blocking()
    {
        // Golden R1/A04: 100 pieces × 12,345 = 1 234,500 kg theoretical; weighed 1 250,400 → +15,900 kg, +1,288 % > 1 %.
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(1m);

        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, PieceReceipt("100", "1250.4"), Metadata("piece-receipt"));

        Assert.Equal(100, runner.Stock!.PieceCount);
        Assert.Equal("1250.4", runner.Stock.QuantityKg);
        Assert.Equal(("100", "1250.4"), (runner.Purchase!.AcceptedQuantity, runner.Purchase.MeasuredQuantityKg));
        Assert.Equal(new PurchaseGoodsReceiptPieceKilogramsV1(100, "12.345", "1234.5", "1250.4", "15.9",
            nameof(GoodsReceiptKgVarianceOutcome.OutsideTolerance), "1"), result.PieceKilograms);
        Assert.Equal(["receipt-read", "purchase", "first-source", "inventory", "fence", "commit"], runner.Events);
    }

    [Fact]
    public async Task A_piece_receipt_exactly_at_the_tolerance_bound_is_within_tolerance()
    {
        // Golden A05 (BS-16 "<="): weighed 1 246,845 = 1 234,5 × 1,01 → exactly 1 %.
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(1m);

        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, PieceReceipt("100", "1246.845"), Metadata("piece-bound"));

        Assert.Equal((nameof(GoodsReceiptKgVarianceOutcome.WithinTolerance), "12.345"),
            (result.PieceKilograms!.VarianceOutcome, result.PieceKilograms.DifferenceKg));
    }

    [Fact]
    public async Task Without_an_Ayarlar_tolerance_a_piece_receipt_reports_tolerance_not_set()
    {
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(null);

        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, PieceReceipt("40", "500.16"), Metadata("piece-noset"));

        // 40 × 12,345 = 493,800 theoretical; weighed 500,160 (golden K02).
        Assert.Equal(("493.8", nameof(GoodsReceiptKgVarianceOutcome.ToleranceNotSet), (string?)null),
            (result.PieceKilograms!.TheoreticalQuantityKg, result.PieceKilograms.VarianceOutcome, result.PieceKilograms.TolerancePercent));
        Assert.Equal(40, runner.Stock!.PieceCount);
    }

    [Fact]
    public async Task A_fraction_of_a_piece_is_refused_before_any_stage()
    {
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(1m);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, PieceReceipt("2.5", "30"), Metadata("piece-fraction")));

        Assert.Equal((400, PurchaseGoodsReceiptCodesV1.PieceCountInvalid), (refusal.Status, refusal.Code));
        Assert.Empty(runner.Events);
    }

    [Fact]
    public async Task A_kilogram_line_receipt_carries_no_piece_count_and_no_weighed_kg_on_the_acceptance()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();

        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, ReceiptRequest(), Metadata("kg-receipt"));

        Assert.Null(runner.Stock!.PieceCount);
        Assert.Null(runner.Purchase!.MeasuredQuantityKg);
        Assert.Null(result.PieceKilograms);
    }

    [Fact]
    public async Task A_piece_line_replay_must_carry_the_same_piece_count_as_the_original_stock_request()
    {
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(1m);
        var request = PieceReceipt("100", "1250.4");
        runner.Original = new PurchaseReceiptStockRequestV1(Company, request.AcceptanceId, orderId, lineId, "4", Product, "7", PieceUnitId,
            request.LotId, request.LabelId, request.LabelCode, null, null, [], "Company", Company, "Company", Company, request.LocationId,
            request.MovementId, request.MeasuredQuantityKg, request.OccurredAtUtc) { PieceCount = 99 };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("piece-replay")));

        Assert.Equal((503, PurchaseGoodsReceiptCodesV1.SourceInvalid), (refusal.Status, refusal.Code));
    }

    // ---- AK-3b: the comparison is recorded with the acceptance and replayed as recorded ---------------------------------------

    private static PurchaseReceiptStockRequestV1 OriginalPieceStock(Guid orderId, Guid lineId, RecordPurchaseGoodsReceiptV1 request, int pieces) =>
        new(Company, request.AcceptanceId, orderId, lineId, "4", Product, "7", PieceUnitId,
            request.LotId, request.LabelId, request.LabelCode, null, null, [], "Company", Company, "Company", Company, request.LocationId,
            request.MovementId, request.MeasuredQuantityKg, request.OccurredAtUtc) { PieceCount = pieces };

    [Fact]
    public async Task A_first_piece_receipt_hands_its_comparison_to_the_acceptance_to_be_recorded()
    {
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(1m);

        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, PieceReceipt("100", "1250.4"), Metadata("piece-record"));

        Assert.NotNull(result.PieceKilograms);
        Assert.Equal(result.PieceKilograms, runner.Purchase!.PieceKilograms);
    }

    [Fact]
    public async Task A_piece_receipt_replay_answers_the_recorded_comparison_not_one_recomputed_with_todays_tolerance()
    {
        // First attempt under a 1 % tolerance: +1,288 % is OutsideTolerance. Ayarlar is later widened to 5 %; the replay must still
        // answer the recorded warning, never "WithinTolerance" at 5 %.
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(1m);
        var request = PieceReceipt("100", "1250.4");
        var first = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("piece-replayed"));
        var recorded = first.PieceKilograms!;
        Assert.Equal((nameof(GoodsReceiptKgVarianceOutcome.OutsideTolerance), "1"), (recorded.VarianceOutcome, recorded.TolerancePercent));

        world.ReceiptPolicy = new FixedReceiptPolicy(new PurchaseGoodsReceiptPolicyV1(5m, null, null));
        runner.Original = OriginalPieceStock(orderId, lineId, request, 100);
        runner.RecordedPieceKilograms = recorded;
        var replay = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("piece-replayed", "retry"));

        Assert.True(replay.IsReplay);
        Assert.Equal(recorded, replay.PieceKilograms);
        Assert.Equal((nameof(GoodsReceiptKgVarianceOutcome.OutsideTolerance), "1"),
            (replay.PieceKilograms!.VarianceOutcome, replay.PieceKilograms.TolerancePercent));
        // A replay sends no new comparison to be recorded.
        Assert.Null(runner.Purchase!.PieceKilograms);
    }

    [Fact]
    public async Task A_piece_receipt_replay_does_not_need_todays_Ayarlar_policy_but_a_first_attempt_does()
    {
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(1m);
        var request = PieceReceipt("100", "1250.4");
        var recorded = (await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("piece-policy"))).PieceKilograms!;

        // Ayarlar now answers an unusable tolerance (fail-closed 503 for a first attempt).
        world.ReceiptPolicy = new FixedReceiptPolicy(new PurchaseGoodsReceiptPolicyV1(150m, null, null));
        runner.Original = OriginalPieceStock(orderId, lineId, request, 100);
        runner.RecordedPieceKilograms = recorded;
        var replay = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("piece-policy", "retry"));
        Assert.Equal(recorded, replay.PieceKilograms);

        var freshRunner = new ReceiptRunner();
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(freshRunner)
            .RecordGoodsReceiptAsync(orderId, lineId, PieceReceipt("10", "123.45") with { AcceptanceId = Guid.NewGuid() }, Metadata("piece-policy-new")));
        Assert.Equal((503, PurchaseGoodsReceiptBatchCodesV1.PolicyInvalid), (refusal.Status, refusal.Code));
        Assert.DoesNotContain("purchase", freshRunner.Events);
    }

    [Fact]
    public async Task A_replay_of_a_piece_receipt_recorded_before_the_comparison_was_stored_answers_none()
    {
        // No comparison was recorded with the older acceptance: nothing is recomputed with today's tolerance.
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(1m);
        var request = PieceReceipt("100", "1250.4");
        runner.Original = OriginalPieceStock(orderId, lineId, request, 100);

        var replay = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("piece-legacy"));

        Assert.True(replay.IsReplay);
        Assert.Null(replay.PieceKilograms);
    }

    [Fact]
    public async Task A_recorded_comparison_that_does_not_describe_the_replayed_receipt_fails_closed()
    {
        var (world, runner, orderId, lineId) = await PieceReceiptWorldAsync(1m);
        var request = PieceReceipt("100", "1250.4");
        runner.Original = OriginalPieceStock(orderId, lineId, request, 100);
        runner.RecordedPieceKilograms = new PurchaseGoodsReceiptPieceKilogramsV1(99, "12.345", "1222.155", "1250.4", "28.245",
            nameof(GoodsReceiptKgVarianceOutcome.OutsideTolerance), "1");

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("piece-mismatch")));

        Assert.Equal((503, PurchaseGoodsReceiptCodesV1.SourceInvalid), (refusal.Status, refusal.Code));
    }

    private sealed class FixedReceiptPolicy(PurchaseGoodsReceiptPolicyV1 policy) : IPurchaseGoodsReceiptPolicyReadPortV1
    {
        public Task<PurchaseGoodsReceiptPolicyV1> ReadAsync(Guid companyId, CancellationToken cancellationToken) => Task.FromResult(policy);
    }
}
