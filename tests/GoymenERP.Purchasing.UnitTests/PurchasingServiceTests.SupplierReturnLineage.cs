using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AK-8 / AK-8b (SAT-D433, SAT-D440–SAT-D445, K3-B2): a supplier return that names a source label other than the receipt's own
/// label. The gate closed keeps the earlier 503; open, the lineage decides between a lineage return (only this supplier's
/// receipts: one part per receipt in ONE atomic document) and a quantity return (any other lineage). Golden rows of
/// <c>ADET_KG_FORMUL_GOLDEN_V1.md</c> section 4 are named in the facts that pin them. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static readonly Guid LineageLabel = Guid.Parse("88888888-8888-4888-8888-000000000001");
    private static readonly Guid OtherSupplier = Guid.Parse("88888888-8888-4888-8888-000000000002");
    private static readonly Guid OtherOrder = Guid.Parse("88888888-8888-4888-8888-000000000003");
    private static readonly Guid InvoiceEvent = Guid.Parse("88888888-8888-4888-8888-000000000004");

    private static CreatePurchaseSupplierReturnV1 LineageReturnRequest(string kg = "1.9") => ReturnRequest() with
    {
        ExpectedPurchaseOrderVersion = null, UndoQuantity = null, ReplacementExpected = false, SourceLabelId = LineageLabel, QuantityKg = kg,
    };

    [Fact]
    public async Task With_the_golden_gate_closed_a_source_label_return_keeps_the_earlier_503_and_reads_nothing()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var lineage = new LineageFake();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnPolicy: new PurchaseSupplierReturnPolicyV1(false, LineageGoldenAccepted: false))
            .CreateSupplierReturnAsync(order, LineageReturnRequest(), Metadata("lineage-gate-closed")));
        var preview = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnPolicy: new PurchaseSupplierReturnPolicyV1(false, LineageGoldenAccepted: false))
            .GetSupplierReturnLineagePreviewAsync(order, Guid.NewGuid(), LineageLabel, "1.9", null));

        Assert.Equal((503, PurchaseSupplierReturnCodesV1.LineageUnavailable), (refusal.Status, refusal.Code));
        Assert.Equal((503, PurchaseSupplierReturnCodesV1.LineageUnavailable), (preview.Status, preview.Code));
        Assert.Equal((0, 0), (lineage.Calls, returns.NewWrites));
    }

    [Fact]
    public async Task A_source_label_return_that_also_adjusts_the_receipt_is_a_request_fault_400_and_reads_nothing()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var lineage = new LineageFake();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage)
            .CreateSupplierReturnAsync(order, ReturnRequest() with { SourceLabelId = LineageLabel }, Metadata("lineage-adjusts")));

        Assert.Equal((400, "validation_failed", 0, 0), (refusal.Status, refusal.Code, lineage.Calls, returns.NewWrites));
    }

    [Fact]
    public async Task The_purchasing_gate_is_open_in_this_train()
    {
        Assert.True(PurchaseSupplierReturnLineageGoldenV1.SupplierReturnLineageGoldenAccepted);
        Assert.True(PurchaseSupplierReturnPolicyV1.Default.LineageGoldenAccepted);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Without_a_wired_lineage_port_a_source_label_return_is_503_and_nothing_is_recorded()
    {
        var (world, order, returns) = await ReturnWorldAsync();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(returns: returns).CreateSupplierReturnAsync(order, LineageReturnRequest(), Metadata("lineage-no-port")));

        Assert.Equal((503, PurchaseSupplierReturnCodesV1.LineageUnavailable), (refusal.Status, refusal.Code));
        Assert.Equal(0, returns.NewWrites);
    }

    [Fact]
    public async Task A_split_label_of_the_named_receipt_is_a_lineage_return_recorded_with_its_source_label_mode_and_one_part()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = LineageReturnRequest();
        Own(returns, order, request.GoodsAcceptanceId);
        var lineage = new LineageFake { Roots = [Root(request.GoodsAcceptanceId, "1", "1", Receipt(request.GoodsAcceptanceId, "600", "600", "1.9"))] };

        var answer = await world.Service(returns: returns, returnLineage: lineage)
            .CreateSupplierReturnAsync(order, request, Metadata("lineage-split"));

        Assert.Equal((LineageLabel, "Lineage", "PendingCostException"), (answer.Return.SourceLabelId, answer.Return.LineageMode, answer.Return.Status));
        var part = Assert.Single(returns.Stored!.Return.LineageParts!);
        Assert.Equal((request.GoodsAcceptanceId, request.ReturnLineId, request.InventoryMovementId, 1.9m, 12.3456m),
            (part.GoodsAcceptanceId, part.ReturnLineId, part.InventoryMovementId, part.QuantityKg, part.UnitCostTryPerKg));
        Assert.Equal(1, lineage.Calls);
    }

    [Fact]
    public async Task I01_a_lineage_of_two_receipts_is_ONE_recorded_return_with_one_exit_part_per_receipt_under_one_key()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var second = Guid.NewGuid();
        var request = TwoReceiptRequest(second, out var secondPart);
        Own(returns, order, request.GoodsAcceptanceId);
        returns.Suppliers[second] = Party;
        var lineage = new LineageFake { Roots = [Root(request.GoodsAcceptanceId, "3", "5", Receipt(request.GoodsAcceptanceId, "600", "600", "180")),
            Root(second, "2", "5", Receipt(second, "400", "400", "120"))] };
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnCosts: new FixedReturnCost(), returnExits: exits);

        var answer = await service.CreateSupplierReturnAsync(order, request, Metadata("lineage-i01"));
        var replay = await service.CreateSupplierReturnAsync(order, request, Metadata("lineage-i01"));

        Assert.Equal(("Recorded", "300", "Lineage", false, true), (answer.Return.Status, answer.Return.QuantityKg, answer.Return.LineageMode,
            answer.IsReplay, replay.IsReplay));
        Assert.Equal(["180", "120"], answer.Return.LineageParts!.Select(part => part.QuantityKg));
        Assert.Equal([order, OtherOrder], answer.Return.LineageParts!.Select(part => part.PurchaseOrderId));
        // One document, one write, one host transaction; the exit request carries both receipts.
        Assert.Equal((1, 1), (returns.NewWrites, exits.Exits.Count));
        var exit = exits.Exits[0];
        Assert.Equal((PurchaseSupplierReturnLineageModeV1.Lineage, "300", 2), (exit.LineageMode, exit.QuantityKg, exit.LineageParts!.Count));
        Assert.Equal([(request.GoodsAcceptanceId, request.InventoryMovementId, 180m, 12.3456m), (second, secondPart.InventoryMovementId, 120m, 12.3456m)],
            exit.LineageParts.Select(part => (part.GoodsAcceptanceId, part.InventoryMovementId, part.QuantityKg, part.UnitCostTryPerKg)));
    }

    [Fact]
    public async Task A_lineage_of_several_receipts_without_its_parts_is_a_recorded_terminal_409_and_replays_as_one()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = LineageReturnRequest("300") with { ManualUnitCostTryPerKg = null };
        var second = Guid.NewGuid();
        Own(returns, order, request.GoodsAcceptanceId);
        returns.Suppliers[second] = Party;
        var lineage = new LineageFake { Roots = [Root(request.GoodsAcceptanceId, "3", "5", Receipt(request.GoodsAcceptanceId, "600", "600", "180")),
            Root(second, "2", "5", Receipt(second, "400", "400", "120"))] };
        var service = world.Service(returns: returns, returnLineage: lineage, returnCosts: new FixedReturnCost(), returnExits: new ReturnExitHost());

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("lineage-multi")));
        var replay = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("lineage-multi")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.LineagePartsMismatch, false), (refusal.Status, refusal.Code, refusal.IsReplay));
        Assert.Equal((409, PurchaseSupplierReturnCodesV1.LineagePartsMismatch, true), (replay.Status, replay.Code, replay.IsReplay));
        Assert.Equal(0, returns.NewWrites);
    }

    [Fact]
    public async Task One_receipt_without_a_readable_invoice_cost_makes_the_whole_document_wait_and_the_approval_stages_every_part()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var second = Guid.NewGuid();
        var request = TwoReceiptRequest(second, out _);
        request = request with { LineageParts = request.LineageParts!.Select(part => part with { ManualUnitCostTryPerKg = "20.5" }).ToArray() };
        Own(returns, order, request.GoodsAcceptanceId);
        returns.Suppliers[second] = Party;
        var lineage = new LineageFake { Roots = [Root(request.GoodsAcceptanceId, "3", "5", Receipt(request.GoodsAcceptanceId, "600", "600", "180")),
            Root(second, "2", "5", Receipt(second, "400", "400", "120"))] };
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: exits);

        var pending = await service.CreateSupplierReturnAsync(order, request, Metadata("lineage-pending"));
        Assert.Equal(("PendingCostException", 0), (pending.Return.Status, exits.Exits.Count));
        var same = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("lineage-self")));
        var approved = await world.AsApprover(() =>
            service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("lineage-approve")));

        Assert.Equal(409, same.Status);
        Assert.Equal(("Recorded", 1, 2), (approved.Return.Status, exits.Exits.Count, exits.Exits[0].LineageParts!.Count));
        Assert.All(exits.Exits[0].LineageParts!, part => Assert.Equal((PurchaseSupplierReturnCostBasis.ManualCostWithException, 20.5m),
            (part.CostBasis, part.UnitCostTryPerKg)));
        Assert.Null(exits.Exits[0].ApprovedCostException);
    }

    [Theory]
    [InlineData(null, null, 409, PurchaseSupplierReturnCodesV1.LineageSelectionRequired)]
    [InlineData("80", "100", 201, null)]
    [InlineData("0", "180", 201, null)]
    [InlineData("180", "0.001", 409, PurchaseSupplierReturnCodesV1.LineagePartsMismatch)]
    [InlineData("70", "100", 409, PurchaseSupplierReturnCodesV1.LineagePartsMismatch)]
    public async Task I04_inside_a_receipt_the_invoiced_and_pending_part_is_the_preparers_explicit_choice(string? pendingKg, string? invoicedKg,
        int status, string? code)
    {
        // R1 has 240 kg invoiced and 360 kg pending left; its share of the 300 kg return is 180 kg (golden I04: 100 + 80).
        var (world, order, returns) = await ReturnWorldAsync();
        var second = Guid.NewGuid();
        var request = TwoReceiptRequest(second, out _);
        if (pendingKg is not null)
            request = request with
            {
                LineageParts = [request.LineageParts![0] with { PendingKg = pendingKg, Invoiced = [new(InvoiceEvent, invoicedKg!)] }, request.LineageParts[1]],
            };
        Own(returns, order, request.GoodsAcceptanceId);
        returns.Suppliers[second] = Party;
        var lineage = new LineageFake { Roots = [Root(request.GoodsAcceptanceId, "3", "5",
            Receipt(request.GoodsAcceptanceId, "600", "600", "180", pending: "360", invoiced: "240")),
            Root(second, "2", "5", Receipt(second, "400", "400", "120"))] };
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnCosts: new FixedReturnCost(), returnExits: exits);

        if (code is null)
        {
            var answer = await service.CreateSupplierReturnAsync(order, request, Metadata("lineage-i04"));
            Assert.Equal((pendingKg, invoicedKg), (answer.Return.LineageParts![0].PendingKg, answer.Return.LineageParts[0].Invoiced![0].QuantityKg));
            Assert.Null(answer.Return.LineageParts[1].PendingKg);
            Assert.Equal(pendingKg, exits.Exits[0].LineageParts![0].PendingKg);
            return;
        }

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("lineage-i04")));
        Assert.Equal((status, code, 0, 0), (refusal.Status, refusal.Code, returns.NewWrites, exits.Exits.Count));
    }

    [Theory]
    [InlineData("100", null, null)]
    [InlineData("600", "30", "250")]
    public async Task I07_a_share_above_the_receipts_open_quantity_or_above_a_chosen_part_is_refused_and_never_moved(string openKg,
        string? pendingKg, string? invoicedKg)
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var second = Guid.NewGuid();
        var request = TwoReceiptRequest(second, out _);
        if (pendingKg is not null)
            request = request with
            {
                LineageParts = [request.LineageParts![0] with { PendingKg = "0", Invoiced = [new(InvoiceEvent, "180")] }, request.LineageParts[1]],
            };
        Own(returns, order, request.GoodsAcceptanceId);
        returns.Suppliers[second] = Party;
        var lineage = new LineageFake { Roots = [Root(request.GoodsAcceptanceId, "3", "5",
            Receipt(request.GoodsAcceptanceId, "600", openKg, "180", pending: pendingKg ?? openKg, invoiced: pendingKg is null ? null : "150")),
            Root(second, "2", "5", Receipt(second, "400", "400", "120"))] };
        var exits = new ReturnExitHost();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnCosts: new FixedReturnCost(), returnExits: exits).CreateSupplierReturnAsync(order, request, Metadata("lineage-i07")));

        _ = invoicedKg;
        Assert.Equal((409, PurchaseSupplierReturnCodesV1.LineageReceiptLimitExceeded, 0, 0),
            (refusal.Status, refusal.Code, returns.NewWrites, exits.Exits.Count));
    }

    [Fact]
    public async Task An_unreadable_part_history_of_a_receipt_is_503_and_nothing_is_recorded()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = LineageReturnRequest();
        Own(returns, order, request.GoodsAcceptanceId);
        var lineage = new LineageFake { Roots = [Root(request.GoodsAcceptanceId, "1", "1",
            Receipt(request.GoodsAcceptanceId, "600", "600", "1.9") with { PartsReadable = false, PendingRemainingKg = null })] };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage)
            .CreateSupplierReturnAsync(order, request, Metadata("lineage-unreadable")));

        Assert.Equal((503, PurchaseSupplierReturnCodesV1.LineageUnavailable, 0), (refusal.Status, refusal.Code, returns.NewWrites));
    }

    [Fact]
    public async Task I10_a_piece_tracking_label_returns_its_pieces_per_receipt_and_a_missing_or_extra_piece_count_is_422()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var request = LineageReturnRequest("100") with { ManualUnitCostTryPerKg = null, PieceCount = 10 };
        request = request with
        {
            LineageParts = [new(request.GoodsAcceptanceId, request.ReturnLineId, request.InventoryMovementId),
                new(second, Guid.NewGuid(), Guid.NewGuid()), new(third, Guid.NewGuid(), Guid.NewGuid())],
        };
        Own(returns, order, request.GoodsAcceptanceId);
        returns.Suppliers[second] = Party;
        returns.Suppliers[third] = Party;
        var lineage = new LineageFake
        {
            TracksPieces = true,
            Roots = [Root(request.GoodsAcceptanceId, "21", "50", Receipt(request.GoodsAcceptanceId, "600", "600", "42", pieces: 5)),
                Root(second, "7", "25", Receipt(second, "400", "400", "28", pieces: 2)),
                Root(third, "3", "10", Receipt(third, "150", "150", "30", pieces: 3))],
        };
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnCosts: new FixedReturnCost(), returnExits: exits);

        var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request with { PieceCount = null }, Metadata("lineage-i10-missing")));
        var answer = await service.CreateSupplierReturnAsync(order, request, Metadata("lineage-i10"));

        Assert.Equal((422, PurchaseSupplierReturnCodesV1.PieceCountRequired), (missing.Status, missing.Code));
        Assert.Equal(10, answer.Return.PieceCount);
        Assert.Equal([(5, "42"), (2, "28"), (3, "30")], answer.Return.LineageParts!.Select(part => (part.PieceCount!.Value, part.QuantityKg)));
        Assert.Equal((10, 3), (exits.Exits[0].PieceCount, exits.Exits[0].LineageParts!.Count));
    }

    [Fact]
    public async Task A_lineage_return_naming_a_receipt_that_is_not_in_the_labels_lineage_is_a_terminal_409()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = LineageReturnRequest();
        var actual = Guid.NewGuid();
        returns.Suppliers[actual] = Party;
        var lineage = new LineageFake { Roots = [Root(actual, "1", "1", Receipt(actual, "600", "600", "1.9"))] };

        var answer = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage)
            .CreateSupplierReturnAsync(order, request, Metadata("lineage-mismatch")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch), (answer.Status, answer.Code));
        Assert.Equal(0, returns.NewWrites);
    }

    [Fact]
    public async Task A_quantity_return_from_an_unpriced_order_line_is_refused_409_because_the_order_price_is_the_supplier_price()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = LineageReturnRequest() with { ManualUnitCostTryPerKg = null };
        var other = Guid.NewGuid();
        returns.Suppliers[other] = OtherSupplier;
        var lineage = new LineageFake { AverageCostReadable = true, Roots = [Root(other, "1", "1")],
            Chosen = Receipt(request.GoodsAcceptanceId, "300", "300", null) };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage)
            .CreateSupplierReturnAsync(order, request, Metadata("quantity-unpriced")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch), (refusal.Status, refusal.Code));
        Assert.Equal(0, returns.NewWrites);
    }

    [Fact]
    public async Task I11_a_quantity_return_with_a_readable_average_is_recorded_at_the_order_price_and_leaves_through_one_exit()
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("20.5"), averageReadable: true);
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: exits);

        var manual = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.CreateSupplierReturnAsync(order,
            request with { ManualUnitCostTryPerKg = "20.5" }, Metadata("quantity-manual")));
        var answer = await service.CreateSupplierReturnAsync(order, request, Metadata("quantity-i11"));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.QuantityAverageAvailable), (manual.Status, manual.Code));
        Assert.Equal(("Recorded", "Quantity", "InvoiceUnitCost", "20.5"),
            (answer.Return.Status, answer.Return.LineageMode, answer.Return.CostBasis, answer.Return.UnitCostTryPerKg));
        var exit = Assert.Single(exits.Exits);
        Assert.Equal((PurchaseSupplierReturnLineageModeV1.Quantity, "20.5", (bool?)null, 1),
            (exit.LineageMode, exit.UnitCostTryPerKg, exit.ApprovedCostException, exit.LineageParts!.Count));
    }

    [Fact]
    public async Task I14_a_quantity_return_above_the_chosen_receipts_open_quantity_is_refused_and_never_moved_to_another_receipt()
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("20.5"), averageReadable: true,
            openKg: "50");
        var exits = new ReturnExitHost();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnExits: exits).CreateSupplierReturnAsync(order, request, Metadata("quantity-i14")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.LineageReceiptLimitExceeded, 0, 0),
            (refusal.Status, refusal.Code, returns.NewWrites, exits.Exits.Count));
    }

    [Fact]
    public async Task SAT_D393_a_quantity_return_whose_average_cannot_be_read_waits_for_another_persons_approval_and_writes_no_value_before_it()
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("20.5"), averageReadable: false);
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: exits);

        var noCost = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("quantity-no-cost")));
        var different = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.CreateSupplierReturnAsync(order,
            request with { ManualUnitCostTryPerKg = "21.25" }, Metadata("quantity-different")));
        var pending = await service.CreateSupplierReturnAsync(order, request with { ManualUnitCostTryPerKg = "20.5" }, Metadata("quantity-pending"));
        Assert.Equal(("PendingCostException", "ManualCostWithException", 0), (pending.Return.Status, pending.Return.CostBasis, exits.Exits.Count));
        var same = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("quantity-self")));
        var approved = await world.AsApprover(() =>
            service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("quantity-approve")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.CostRequired), (noCost.Status, noCost.Code));
        Assert.Equal((409, PurchaseSupplierReturnCodesV1.QuantityManualCostDifference), (different.Status, different.Code));
        Assert.Equal((409, 0), (same.Status, exits.Exits.Count - 1));
        Assert.Equal(("Recorded", true), (approved.Return.Status, Assert.Single(exits.Exits).ApprovedCostException));
    }

    [Theory]
    [InlineData("20.5", null, null, null, "20.5")]
    [InlineData("20.5", "2.5", null, null, "19.9875")]
    [InlineData("20.5", null, "0.5", null, "20")]
    [InlineData("20.5", null, null, "3", "20.5")]
    [InlineData("20.5", "2.5", null, "3", "19.9875")]
    public async Task SAT_D451_a_quantity_return_bills_the_lines_net_price_after_the_discount_and_never_the_deferral(string unitPrice,
        string? discountPercent, string? discountAmount, string? deferralPercent, string expected)
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(
            new PurchaseOrderLinePriceRequest(unitPrice, DiscountPercent: discountPercent, DiscountAmount: discountAmount, DeferralPercent: deferralPercent),
            averageReadable: true);
        var exits = new ReturnExitHost();

        var answer = await world.Service(returns: returns, returnLineage: lineage, returnExits: exits)
            .CreateSupplierReturnAsync(order, request, Metadata("quantity-sat-d451"));

        Assert.Equal(("Recorded", expected), (answer.Return.Status, answer.Return.UnitCostTryPerKg));
        Assert.Equal(expected, Assert.Single(exits.Exits).UnitCostTryPerKg);
    }

    [Fact]
    public async Task A_net_price_with_more_than_four_decimals_is_refused_422_and_never_rounded()
    {
        // 41.25 less 2.5 % is 40.21875: five decimals. The stock exit carries four; rounding it is not decided.
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(
            new PurchaseOrderLinePriceRequest("41.25", DiscountPercent: "2.5"), averageReadable: true);
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: exits);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("quantity-five-decimals")));
        var preview = await service.GetSupplierReturnLineagePreviewAsync(order, request.GoodsAcceptanceId, LineageLabel, request.QuantityKg, null);

        Assert.Equal((422, PurchaseSupplierReturnCodesV1.QuantityPriceUndecided, 0, 0), (refusal.Status, refusal.Code, returns.NewWrites, exits.Exits.Count));
        Assert.Equal(("Quantity", PurchaseSupplierReturnCodesV1.QuantityPriceUndecided), (preview.LineageMode, preview.Refusal));
    }

    [Theory]
    [InlineData(true, true, true, "20.5", null, null, "Priced", "20.5")]
    [InlineData(true, true, true, "20.5", "2.5", null, "Priced", "19.9875")]
    [InlineData(true, true, true, "20.5", null, "0.5", "Priced", "20")]
    [InlineData(false, true, true, "20.5", null, null, "NotAKilogramTryLine", null)]
    [InlineData(true, false, true, "20.5", null, null, "NotAKilogramTryLine", null)]
    [InlineData(true, true, false, "20.5", null, null, "NotAKilogramTryLine", null)]
    [InlineData(true, true, true, null, null, null, "NotAKilogramTryLine", null)]
    [InlineData(true, true, true, "20.5", "100", null, "NotAKilogramTryLine", null)]
    [InlineData(true, true, true, "41.25", "2.5", null, "NotRepresentable", null)]
    [InlineData(true, true, true, "20.50001", null, null, "NotRepresentable", null)]
    public void SAT_D451_the_single_price_rule_point_is_the_net_price_after_the_discount_of_a_kilogram_line_in_TL(bool lira, bool kilogram,
        bool perUnit, string? price, string? discountPercent, string? discountAmount, string outcome, string? expected)
    {
        static decimal? Number(string? text) => text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var answer = PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(new(lira, kilogram, perUnit, Number(price), Number(discountPercent),
            Number(discountAmount)));

        Assert.Equal(outcome, answer.Outcome.ToString());
        Assert.Equal(Number(expected), answer.UnitPriceTryPerKg);
        Assert.Contains("SAT-D451", PurchaseSupplierReturnQuantityPriceRuleV1.DiscountedLinePriceDecision, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_preview_shows_each_receipts_share_open_parts_and_cost_source_without_a_price_and_names_the_refusal()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var acceptance = Guid.NewGuid();
        var second = Guid.NewGuid();
        Own(returns, order, acceptance);
        returns.Suppliers[second] = Party;
        var lineage = new LineageFake { FreeKg = "300", Roots = [Root(acceptance, "3", "5", Receipt(acceptance, "600", "100", "180", pending: "40", invoiced: "60")),
            Root(second, "2", "5", Receipt(second, "400", "400", "120"))] };

        var preview = await world.Service(returns: returns, returnLineage: lineage, returnCosts: new FixedReturnCost())
            .GetSupplierReturnLineagePreviewAsync(order, acceptance, LineageLabel, "300", null);

        Assert.Equal(("Lineage", "300", "300", false, PurchaseSupplierReturnCodesV1.LineageReceiptLimitExceeded),
            (preview.LineageMode, preview.QuantityKg, preview.FreeKg, preview.TracksPieces, preview.Refusal));
        Assert.Equal([("180", "100", "40", true, "InvoiceUnitCost"), ("120", "400", "400", false, "InvoiceUnitCost")],
            preview.Parts.Select(part => (part.QuantityKg, part.OpenKg, part.PendingRemainingKg, part.SelectionRequired, part.CostSource)));
        Assert.Equal("60", Assert.Single(preview.Parts[0].Invoiced).RemainingKg);
        Assert.DoesNotContain("12.3456", System.Text.Json.JsonSerializer.Serialize(preview));
    }

    [Theory]
    [InlineData("only-supplier-single", "Lineage")]
    [InlineData("only-supplier-several", "Lineage")]
    [InlineData("other-supplier", "Quantity")]
    [InlineData("production-half", "Quantity")]
    [InlineData("no-receipt", "Quantity")]
    public void SAT_D445_the_planner_distributes_only_a_lineage_of_the_supplier_s_own_receipts(string scenario, string mode)
    {
        var mine = Guid.NewGuid();
        var also = Guid.NewGuid();
        var other = Guid.NewGuid();
        var suppliers = new Dictionary<Guid, Guid?> { [mine] = Party, [also] = Party, [other] = OtherSupplier };
        PurchaseSupplierReturnLineageRootV1[] roots = scenario switch
        {
            "only-supplier-single" => [Root(mine, "1", "1")],
            "only-supplier-several" => [Root(mine, "3", "5"), Root(also, "2", "5")],
            "other-supplier" => [Root(other, "1", "1")],
            "production-half" => [Root(mine, "1", "2"), new(false, null, "1", "2")],
            _ => [new(false, null, "1", "1")],
        };

        var plan = PurchaseSupplierReturnLineagePlannerV1.Plan(new(LineageLabel, roots), suppliers, Party, mine);

        Assert.Equal(mode, plan.Mode.ToString());
    }

    [Fact]
    public void The_planner_refuses_shares_that_do_not_add_up_and_an_unknown_supplier_without_guessing()
    {
        var mine = Guid.NewGuid();
        Assert.Equal(503, Assert.Throws<PurchasingServiceException>(() => PurchaseSupplierReturnLineagePlannerV1.Plan(
            new(LineageLabel, [Root(mine, "1", "2")]), new Dictionary<Guid, Guid?> { [mine] = Party }, Party, mine)).Status);
        Assert.Equal(503, Assert.Throws<PurchasingServiceException>(() => PurchaseSupplierReturnLineagePlannerV1.Plan(
            new(LineageLabel, [Root(mine, "1", "1")]), new Dictionary<Guid, Guid?> { [mine] = null }, Party, mine)).Status);
    }

    [Fact]
    public void The_posted_parts_must_name_the_documents_own_receipt_with_the_documents_line_and_movement()
    {
        var request = LineageReturnRequest();
        var own = new PurchaseSupplierReturnLineagePartRequestV1(request.GoodsAcceptanceId, request.ReturnLineId, request.InventoryMovementId);
        var other = new PurchaseSupplierReturnLineagePartRequestV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        PurchaseSupplierReturnJsonV1.ValidateCreate(request with { LineageParts = [own, other] });
        Assert.Throws<System.Text.Json.JsonException>(() => PurchaseSupplierReturnJsonV1.ValidateCreate(request with { LineageParts = [other] }));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchaseSupplierReturnJsonV1.ValidateCreate(
            request with { LineageParts = [own with { ReturnLineId = Guid.NewGuid() }, other] }));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchaseSupplierReturnJsonV1.ValidateCreate(
            request with { LineageParts = [own with { PendingKg = "1" }, other] }));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchaseSupplierReturnJsonV1.ValidateCreate(
            request with { SourceLabelId = null, PieceCount = 3 }));
    }

    private static CreatePurchaseSupplierReturnV1 TwoReceiptRequest(Guid second, out PurchaseSupplierReturnLineagePartRequestV1 secondPart)
    {
        var request = LineageReturnRequest("300") with { ManualUnitCostTryPerKg = null };
        secondPart = new PurchaseSupplierReturnLineagePartRequestV1(second, Guid.NewGuid(), Guid.NewGuid());
        return request with
        {
            LineageParts = [new(request.GoodsAcceptanceId, request.ReturnLineId, request.InventoryMovementId), secondPart],
        };
    }

    private static async Task<(World World, Guid Order, ReturnStoreFake Returns, CreatePurchaseSupplierReturnV1 Request, LineageFake Lineage)>
        QuantityWorldAsync(PurchaseOrderLinePriceRequest price, bool averageReadable, string openKg = "300")
    {
        var world = new World();
        var order = await world.Service().CreateOrderAsync(PricedOrderOf("300", price), Metadata("quantity-order"));
        await world.AsApprover(() => world.Service().ApproveOrderAsync(order.Detail.Order.Id, new("2"), Metadata("quantity-order-approve")));
        var returns = new ReturnStoreFake(order.Detail.Lines[0].LineId);
        var request = LineageReturnRequest("80") with { ManualUnitCostTryPerKg = null };
        var foreign = Guid.NewGuid();
        returns.Suppliers[foreign] = OtherSupplier;
        // Golden I11: the label's lineage is another supplier's receipt; the chosen receipt R5 of this order has 300 kg open.
        var lineage = new LineageFake { AverageCostReadable = averageReadable, Roots = [Root(foreign, "1", "1")],
            Chosen = Receipt(request.GoodsAcceptanceId, "300", openKg, null) };
        return (world, order.Detail.Order.Id, returns, request, lineage);
    }

    private static void Own(ReturnStoreFake returns, Guid order, Guid acceptance)
    {
        returns.Suppliers[acceptance] = Party;
        returns.Orders[acceptance] = order;
    }

    private static PurchaseSupplierReturnLineageRootV1 Root(Guid acceptance, string numerator, string denominator,
        PurchaseSupplierReturnReceiptStateV1? receipt = null) => new(true, acceptance, numerator, denominator, receipt);

    private static PurchaseSupplierReturnReceiptStateV1 Receipt(Guid acceptance, string received, string open, string? distributed,
        string? pending = null, string? invoiced = null, int? pieces = null) => new(acceptance, received, open, true, pending ?? open,
        invoiced is null ? [] : [new PurchaseSupplierReturnInvoicedPartV1(InvoiceEvent, Guid.NewGuid(), Guid.NewGuid(), invoiced)],
        distributed, pieces);

    private sealed class LineageFake : IPurchaseSupplierReturnLineageReadPortV1
    {
        public IReadOnlyList<PurchaseSupplierReturnLineageRootV1> Roots { get; init; } = [];
        public PurchaseSupplierReturnReceiptStateV1? Chosen { get; init; }
        public bool TracksPieces { get; init; }
        public bool AverageCostReadable { get; init; }
        public string FreeKg { get; init; } = "1000";
        public int Calls { get; private set; }

        public Task<PurchaseSupplierReturnLineageV1> PlanAsync(Guid companyId, Guid labelId, string quantityKg, int? pieceCount,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new PurchaseSupplierReturnLineageV1(labelId, Roots, TracksPieces, FreeKg, "0",
                TracksPieces ? 100 : null, TracksPieces ? 0 : null, AverageCostReadable));
        }

        public Task<PurchaseSupplierReturnReceiptStateV1> ReadReceiptAsync(Guid companyId, Guid goodsAcceptanceId, CancellationToken cancellationToken) =>
            Task.FromResult(Chosen is { } chosen ? chosen with { GoodsAcceptanceId = goodsAcceptanceId }
                : throw new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.LineageUnavailable));
    }

    private sealed class ReturnExitHost : IPurchaseSupplierReturnTransactionV1, IPurchaseSupplierReturnExitV1
    {
        public List<PurchaseSupplierReturnExitRequestV1> Exits { get; } = [];

        public async Task<T> ExecuteAsync<T>(Guid companyId, Func<IPurchaseSupplierReturnExitV1, CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
        {
            var result = await operation(this, cancellationToken);
            await beforeCommit(cancellationToken);
            return result;
        }

        public Task<PurchaseSupplierReturnExitResultV1> StageExitAsync(PurchaseSupplierReturnExitRequestV1 request, PurchasingWriteEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Exits.Add(request);
            return Task.FromResult(new PurchaseSupplierReturnExitResultV1(request.InventoryMovementId, LineageLabel, request.QuantityKg, false));
        }
    }
}
