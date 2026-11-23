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
        var exits = new ReturnExitHost { StagedAverage = "20.5" };
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: exits);

        var manual = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.CreateSupplierReturnAsync(order,
            request with { ManualUnitCostTryPerKg = "20.5" }, Metadata("quantity-manual")));
        var answer = await service.CreateSupplierReturnAsync(order, request, Metadata("quantity-i11"));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.QuantityAverageAvailable), (manual.Status, manual.Code));
        Assert.Equal(("Recorded", "Quantity", "InvoiceUnitCost", "20.5"),
            (answer.Return.Status, answer.Return.LineageMode, answer.Return.CostBasis, answer.Return.UnitCostTryPerKg));
        var exit = Assert.Single(exits.Exits);
        // SAT-D456: Inventory measures the difference on the billed amount (80 × 20.5) and leaves stock at the average it reads.
        Assert.Equal((PurchaseSupplierReturnLineageModeV1.Quantity, "", 1640m, (bool?)null, 1),
            (exit.LineageMode, exit.UnitCostTryPerKg, exit.SupplierReturnAmountTry, exit.ApprovedCostException, exit.LineageParts!.Count));
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

        var answer = await world.Service(returns: returns, returnLineage: lineage, returnExits: exits, returnPolicy: QuantityRulesClosed)
            .CreateSupplierReturnAsync(order, request, Metadata("quantity-sat-d451"));

        Assert.Equal(("Recorded", expected), (answer.Return.Status, answer.Return.UnitCostTryPerKg));
        Assert.Equal(expected, Assert.Single(exits.Exits).UnitCostTryPerKg);
    }

    [Fact]
    public async Task With_the_quantity_rules_gate_closed_a_net_price_with_more_than_four_decimals_is_refused_422_and_never_rounded()
    {
        // 41.25 less 2.5 % is 40.21875: five decimals. The stock exit carries four; rounding it is not decided.
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(
            new PurchaseOrderLinePriceRequest("41.25", DiscountPercent: "2.5"), averageReadable: true);
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: exits, returnPolicy: QuantityRulesClosed);

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
            Number(discountAmount)), quantityRulesAccepted: false);

        Assert.Equal(outcome, answer.Outcome.ToString());
        Assert.Equal(Number(expected), answer.UnitPriceTryPerKg);
        Assert.Contains("SAT-D451", PurchaseSupplierReturnQuantityPriceRuleV1.DiscountedLinePriceDecision, StringComparison.Ordinal);
    }

    private static readonly PurchaseSupplierReturnPolicyV1 QuantityRulesOpen = new(false, QuantityRulesGoldenAccepted: true);
    private static readonly PurchaseSupplierReturnPolicyV1 QuantityRulesClosed = new(false, QuantityRulesGoldenAccepted: false);

    [Fact]
    public void The_SAT_D453_to_D461_quantity_rules_gate_is_open_with_golden_EK1_in_the_train()
    {
        // docs/decisions/ADET_KG_FORMUL_GOLDEN_V1_EK1.md rows E01–E15; the closed state stays pinned through the policy parameter.
        Assert.True(PurchaseSupplierReturnLineageGoldenV1.SupplierReturnQuantityRulesGoldenAccepted);
        Assert.True(PurchaseSupplierReturnPolicyV1.Default.QuantityRulesGoldenAccepted);
        Assert.False(QuantityRulesClosed.QuantityRulesGoldenAccepted);
    }

    [Theory]
    [InlineData("41.25", "2.5", "40.2188")]   // 40.21875 → half away from zero
    [InlineData("41.23", "2.5", "40.1993")]   // 40.19925 → half away from zero
    [InlineData("10.0001", "3", "9.7001")]    // 9.700097 → down
    [InlineData("20.5", "2.5", "19.9875")]    // already four decimals: unchanged
    public void SAT_D453_with_the_gate_open_a_net_price_above_four_decimals_is_rounded_to_four_half_away_from_zero(string unitPrice,
        string discountPercent, string expected)
    {
        static decimal Number(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var line = new PurchaseSupplierReturnQuantityPriceLineV1(true, true, true, Number(unitPrice), Number(discountPercent));

        var open = PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(line, quantityRulesAccepted: true);
        var closed = PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(line, quantityRulesAccepted: false);

        Assert.Equal((PurchaseSupplierReturnQuantityPriceOutcomeV1.Priced, Number(expected)), (open.Outcome, open.UnitPriceTry));
        Assert.Equal(expected == "19.9875" ? PurchaseSupplierReturnQuantityPriceOutcomeV1.Priced : PurchaseSupplierReturnQuantityPriceOutcomeV1.NotRepresentable,
            closed.Outcome);
    }

    [Fact]
    public async Task SAT_D453_D456_with_the_gate_open_the_document_bills_the_rounded_net_price_and_records_the_average_stock_left_at()
    {
        // 80 kg at 41.25 less 2.5 % = 40.21875 → 40.2188 TL/kg; the document bills 80 × 40.2188 = 3 217.50 TL (kuruş).
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(
            new PurchaseOrderLinePriceRequest("41.25", DiscountPercent: "2.5"), averageReadable: true);
        var exits = new ReturnExitHost { StagedAverage = "40.2187" };

        var answer = await world.Service(returns: returns, returnLineage: lineage, returnExits: exits, returnPolicy: QuantityRulesOpen)
            .CreateSupplierReturnAsync(order, request, Metadata("quantity-sat-d453"));

        var exit = Assert.Single(exits.Exits);
        // Inventory is asked to measure the difference on the billed amount and names no unit cost of its own caller.
        Assert.Equal((3217.50m, ""), (exit.SupplierReturnAmountTry, exit.UnitCostTryPerKg));
        var part = Assert.Single(answer.Return.LineageParts!);
        Assert.Equal(("Recorded", "40.2187", "40.2187", "40.2188", "3217.5"),
            (answer.Return.Status, answer.Return.UnitCostTryPerKg, part.UnitCostTryPerKg, part.SupplierUnitPriceTry, part.SupplierAmountTry));
    }

    [Fact]
    public async Task SAT_D456_with_the_gate_open_a_recorded_return_without_the_owners_stock_cost_is_refused_and_nothing_is_saved()
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("20.5"), averageReadable: true);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnExits: new ReturnExitHost(), returnPolicy: QuantityRulesOpen).CreateSupplierReturnAsync(order, request, Metadata("d456-no-cost")));

        Assert.Equal((503, PurchaseSupplierReturnCodesV1.Unavailable, 0), (refusal.Status, refusal.Code, returns.NewWrites));
    }

    [Theory]
    [InlineData("80", "40.2188", "3217.5")]
    [InlineData("4", "205", "820")]
    [InlineData("3", "0.335", "1.01")]     // 1.005 → half away from zero
    [InlineData("33.333", "19.9936", "666.45")]
    public void SAT_D453_D456_the_document_amount_is_the_quantity_times_the_rounded_price_rounded_once_to_kurus(string quantity, string price,
        string expected)
    {
        static decimal Number(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Number(expected), PurchaseSupplierReturnQuantityPriceRuleV1.Amount(Number(quantity), Number(price)));
    }

    [Theory]
    [InlineData(true, "DocumentDiscount")]
    [InlineData(false, "Priced")]
    public void SAT_D458_a_document_level_discount_stops_the_quantity_return_behind_the_gate_and_line_discounts_are_all_deducted(bool gateOpen,
        string outcome)
    {
        var line = new PurchaseSupplierReturnQuantityPriceLineV1(true, true, true, 20.5m, 2.5m, null, HasDocumentDiscount: true);

        Assert.Equal(outcome, PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(line, gateOpen).Outcome.ToString());
        // The line's own discount (percent or amount; the order line has no chain) is always deducted in full.
        Assert.Equal(19.9875m, PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(line with { HasDocumentDiscount = false }, gateOpen).UnitPriceTry);
        Assert.Equal(20m, PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(
            new PurchaseSupplierReturnQuantityPriceLineV1(true, true, true, 20.5m, null, 0.5m), gateOpen).UnitPriceTry);
    }

    [Fact]
    public async Task SAT_D454_D461_with_the_gate_open_an_approved_manual_cost_may_differ_as_a_unit_cost_only_while_the_kurus_amounts_are_equal()
    {
        // 3 kg at 20.5 TL/kg bills 61.50 TL. A manual cost of 20.5005 leaves 61.5015 → 61.50 TL: no kuruş difference (SAT-D456),
        // so it waits for the other person's approval; 21.25 leaves 63.75 TL and waits on the difference (SAT-D461).
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("20.5"), averageReadable: false);
        request = request with { QuantityKg = "3" };
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: exits, returnPolicy: QuantityRulesOpen);

        var noCost = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("d454-no-cost")));
        var different = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request with { ManualUnitCostTryPerKg = "21.25" }, Metadata("d461-different")));
        Assert.Equal(0, returns.NewWrites);
        var pending = await service.CreateSupplierReturnAsync(order, request with { ManualUnitCostTryPerKg = "20.5005" }, Metadata("d454-pending"));
        Assert.Equal(("PendingCostException", "ManualCostWithException", "20.5005", 0),
            (pending.Return.Status, pending.Return.CostBasis, pending.Return.UnitCostTryPerKg, exits.Exits.Count));
        Assert.Equal(("20.5005", "20.5", "61.5"), (pending.Return.LineageParts![0].UnitCostTryPerKg, pending.Return.LineageParts[0].SupplierUnitPriceTry,
            pending.Return.LineageParts[0].SupplierAmountTry));
        var same = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("d454-self")));
        var approved = await world.AsApprover(() =>
            service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("d454-approve")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.CostRequired, 409), (noCost.Status, noCost.Code, same.Status));
        Assert.Equal((409, PurchaseSupplierReturnCodesV1.QuantityManualCostDifference), (different.Status, different.Code));
        // Stock leaves at the approved manual cost; the supplier price stays the order's net price on the document.
        var exit = Assert.Single(exits.Exits);
        Assert.Equal(("Recorded", "20.5005", true, 20.5m, 61.5m), (approved.Return.Status, exit.UnitCostTryPerKg, exit.ApprovedCostException,
            exit.LineageParts![0].SupplierUnitPriceTry, exit.SupplierReturnAmountTry));
    }

    [Fact]
    public async Task SAT_D454_with_the_gate_open_a_manual_cost_is_still_refused_while_the_average_is_readable()
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("20.5"), averageReadable: true);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnExits: new ReturnExitHost(), returnPolicy: QuantityRulesOpen)
            .CreateSupplierReturnAsync(order, request with { ManualUnitCostTryPerKg = "21.25" }, Metadata("d454-readable")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.QuantityAverageAvailable, 0), (refusal.Status, refusal.Code, returns.NewWrites));
    }

    [Fact]
    public async Task SAT_D455_with_the_gate_closed_a_piece_order_line_keeps_todays_409_with_or_without_a_piece_quantity()
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("205", PieceUnitId),
            averageReadable: true, pieceLine: true);
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: new ReturnExitHost(), returnPolicy: QuantityRulesClosed);

        var plain = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("d455-closed")));
        var withPieces = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request with { SupplierPieceQuantity = 4 }, Metadata("d455-closed-pieces")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch), (plain.Status, plain.Code));
        Assert.Equal((409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch, 0), (withPieces.Status, withPieces.Code, returns.NewWrites));
    }

    [Fact]
    public async Task SAT_D455_D459_with_the_gate_open_a_piece_order_line_bills_the_labels_pieces_at_the_net_piece_price()
    {
        // 80 kg and 4 pieces leave the label; the document bills 4 pieces at 205 TL = 820.00 TL; the kilograms are information.
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("205", PieceUnitId),
            averageReadable: true, pieceLine: true, tracksPieces: true);
        request = request with { PieceCount = 4 };
        var exits = new ReturnExitHost { StagedAverage = "10.2531" };
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: exits, returnPolicy: QuantityRulesOpen);

        var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("d455-missing")));
        var different = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request with { SupplierPieceQuantity = 3 }, Metadata("d459-different")));
        var answer = await service.CreateSupplierReturnAsync(order, request with { SupplierPieceQuantity = 4 }, Metadata("d455"));

        Assert.Equal((422, PurchaseSupplierReturnCodesV1.PieceCountRequired), (missing.Status, missing.Code));
        Assert.Equal((422, PurchaseSupplierReturnCodesV1.QuantityPieceQuantityMismatch), (different.Status, different.Code));
        var part = Assert.Single(answer.Return.LineageParts!);
        Assert.Equal(("Recorded", "80", 4, "205", 4, "820", "10.2531"), (answer.Return.Status, part.QuantityKg, part.PieceCount, part.SupplierUnitPriceTry,
            part.SupplierPieceQuantity, part.SupplierAmountTry, part.UnitCostTryPerKg));
        Assert.Equal((820m, "", 4), (Assert.Single(exits.Exits).SupplierReturnAmountTry, exits.Exits[0].UnitCostTryPerKg, exits.Exits[0].PieceCount));
    }

    [Fact]
    public async Task SAT_D459_with_the_gate_open_a_label_that_tracks_no_pieces_cannot_carry_a_piece_line_return()
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("205", PieceUnitId),
            averageReadable: true, pieceLine: true);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnExits: new ReturnExitHost(), returnPolicy: QuantityRulesOpen)
            .CreateSupplierReturnAsync(order, request with { SupplierPieceQuantity = 4 }, Metadata("d459-no-pieces")));

        Assert.Equal((422, PurchaseSupplierReturnCodesV1.QuantityPieceQuantityMismatch, 0), (refusal.Status, refusal.Code, returns.NewWrites));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_kilogram_order_line_or_a_lineage_return_never_names_a_document_piece_quantity(bool gateOpen)
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest("20.5"), averageReadable: true);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnExits: new ReturnExitHost(), returnPolicy: gateOpen ? QuantityRulesOpen : QuantityRulesClosed)
            .CreateSupplierReturnAsync(order, request with { SupplierPieceQuantity = 4 }, Metadata("d455-kg-line")));

        Assert.Equal((400, "validation_failed", 0), (refusal.Status, refusal.Code, returns.NewWrites));
    }

    // ---- Golden EK1 (docs/decisions/ADET_KG_FORMUL_GOLDEN_V1_EK1.md), rows pinned by their row id. ----

    [Theory]
    // row, per piece, unit price, discount %, discount amount, quantity (kg or pieces) → rounded net unit price, document amount
    [InlineData("E01", false, "41.25", "2.5", null, "1000", "40.2188", "40218.80")]
    [InlineData("E02", false, "10.01", "7.5", null, "100", "9.2593", "925.93")]
    [InlineData("E03", false, "20.50", null, null, "80", "20.5000", "1640.00")]
    [InlineData("E04", false, "41.25", "2.5", null, "80", "40.2188", "3217.50")]
    [InlineData("E05", false, "40.00", "2.5", null, "50", "39.0000", "1950.00")]
    [InlineData("E06", false, "21.00", "2.5", null, "80", "20.4750", "1638.00")]
    [InlineData("E07a", false, "41.25", null, "0.2500", "80", "41.0000", "3280.00")]
    [InlineData("E12", true, "265.13", "2.5", null, "3", "258.5018", "775.51")]
    [InlineData("E13", true, "265.13", "2.5", null, "3", "258.5018", "775.51")]
    [InlineData("E13a", true, "300.00", null, null, "3", "300.0000", "900.00")]
    [InlineData("E13b", true, "265.35", null, null, "3", "265.3500", "796.05")]
    public void Golden_EK1_the_net_unit_price_is_rounded_to_four_decimals_half_away_from_zero_and_the_document_amount_once_to_kurus(string row,
        bool perPiece, string unitPrice, string? discountPercent, string? discountAmount, string quantity, string expectedPrice, string expectedAmount)
    {
        static decimal? Number(string? text) => text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        // SAT-D451: the maturity difference of E04 (3 %) and E13 (2 %) is not a fact of the rule at all; it cannot enter the price.
        var line = new PurchaseSupplierReturnQuantityPriceLineV1(true, !perPiece, true, Number(unitPrice), Number(discountPercent),
            Number(discountAmount), IsPieceLine: perPiece);

        var price = PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(line, quantityRulesAccepted: true);

        Assert.True(price.Outcome == PurchaseSupplierReturnQuantityPriceOutcomeV1.Priced, row);
        Assert.Equal((row, Number(expectedPrice), perPiece), (row, price.UnitPriceTry, price.PerPiece));
        Assert.Equal((row, Number(expectedAmount)),
            (row, PurchaseSupplierReturnQuantityPriceRuleV1.Amount(Number(quantity)!.Value, price.UnitPriceTry!.Value)));
    }

    [Theory]
    [InlineData("E01", "40218.75")]   // the exact net 40.21875 × 1000: must NOT be the document amount
    [InlineData("E02", "9.2592")]     // banker's rounding of 9.25925: must NOT be the unit price
    public void Golden_EK1_the_rejected_readings_of_E01_and_E02_are_not_produced(string row, string rejected)
    {
        var wrong = decimal.Parse(rejected, System.Globalization.CultureInfo.InvariantCulture);
        var price = row == "E01"
            ? PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(new(true, true, true, 41.25m, 2.5m), true).UnitPriceTry!.Value
            : PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(new(true, true, true, 10.01m, 7.5m), true).UnitPriceTry!.Value;

        Assert.NotEqual(wrong, row == "E01" ? PurchaseSupplierReturnQuantityPriceRuleV1.Amount(1000m, price) : price);
    }

    [Theory]
    [InlineData("E07b", null, null, true, "DocumentDiscount")]   // SAT-D458: a document-level discount stops the return
    [InlineData("E07c", "2.5", "0.25", false, "NotRepresentable")] // percent + amount on one line: not in the model, stays closed
    public void Golden_EK1_a_document_discount_or_a_discount_shape_outside_the_line_model_never_prices_a_quantity_return(string row,
        string? discountPercent, string? discountAmount, bool documentDiscount, string outcome)
    {
        static decimal? Number(string? text) => text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var answer = PurchaseSupplierReturnQuantityPriceRuleV1.Resolve(new(true, true, true, 41.25m, Number(discountPercent),
            Number(discountAmount), HasDocumentDiscount: documentDiscount), quantityRulesAccepted: true);

        Assert.Equal((row, outcome, (decimal?)null), (row, answer.Outcome.ToString(), answer.UnitPriceTry));
    }

    [Theory]
    // row, unit price, discount %, discount amount, maturity difference % → what the document bills for 80 kg
    [InlineData("E04", "41.25", "2.5", null, "3", "40.2188", "3217.5")]
    [InlineData("E07a", "41.25", null, "0.25", null, "41", "3280")]
    public async Task Golden_EK1_a_kilogram_line_quantity_return_bills_the_rounded_net_price_without_the_maturity_difference(string row,
        string unitPrice, string? discountPercent, string? discountAmount, string? deferralPercent, string expectedPrice, string expectedAmount)
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(new PurchaseOrderLinePriceRequest(unitPrice,
            DiscountPercent: discountPercent, DiscountAmount: discountAmount, DeferralPercent: deferralPercent), averageReadable: true);
        var exits = new ReturnExitHost { StagedAverage = expectedPrice };

        var answer = await world.Service(returns: returns, returnLineage: lineage, returnExits: exits)
            .CreateSupplierReturnAsync(order, request, Metadata("golden-ek1-kg"));

        var part = Assert.Single(answer.Return.LineageParts!);
        Assert.Equal((row, "Recorded", expectedPrice, expectedAmount), (row, answer.Return.Status, part.SupplierUnitPriceTry, part.SupplierAmountTry));
        Assert.Equal(decimal.Parse(expectedAmount, System.Globalization.CultureInfo.InvariantCulture), Assert.Single(exits.Exits).SupplierReturnAmountTry);
    }

    [Theory]
    // row, maturity difference % → 3 pieces / 37.461 kg leave the label; the document bills 3 × 258.5018 = 775.51 TL
    [InlineData("E12", null)]
    [InlineData("E13", "2")]
    public async Task Golden_EK1_a_piece_line_quantity_return_bills_the_labels_pieces_at_the_rounded_net_piece_price(string row, string? deferralPercent)
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(
            new PurchaseOrderLinePriceRequest("265.13", PieceUnitId, DiscountPercent: "2.5", DeferralPercent: deferralPercent),
            averageReadable: true, pieceLine: true, tracksPieces: true);
        request = request with { QuantityKg = "37.461", PieceCount = 3, SupplierPieceQuantity = 3 };
        var exits = new ReturnExitHost { StagedAverage = "21.25" };

        var answer = await world.Service(returns: returns, returnLineage: lineage, returnExits: exits)
            .CreateSupplierReturnAsync(order, request, Metadata("golden-ek1-piece"));

        var part = Assert.Single(answer.Return.LineageParts!);
        Assert.Equal((row, "37.461", 3, "258.5018", 3, "775.51", "21.25"), (row, part.QuantityKg, part.PieceCount, part.SupplierUnitPriceTry,
            part.SupplierPieceQuantity, part.SupplierAmountTry, part.UnitCostTryPerKg));
        // The kilograms are information on the document; Inventory compares 775.51 TL with the stock exit amount (E12: 796.05).
        Assert.Equal((775.51m, "", 3), (Assert.Single(exits.Exits).SupplierReturnAmountTry, exits.Exits[0].UnitCostTryPerKg, exits.Exits[0].PieceCount));
    }

    [Theory]
    // row, piece line, unit price, discount %, kg, pieces, manual cost → 409 on a kuruş difference, otherwise it waits for approval
    [InlineData("E08", false, "21.00", "2.5", "80", null, "22.1", false)]      // 1 768.00 − 1 638.00 = 130.00
    [InlineData("E09", false, "21.00", "2.5", "80", null, "20.475", true)]       // 1 638.00 − 1 638.00 = 0
    [InlineData("E14", true, "265.13", "2.5", "37.461", 3, "20.7", false)]     // 775.44 − 775.51 = −0.07 (not −0.06)
    public async Task Golden_EK1_with_an_unreadable_average_a_manual_cost_proceeds_to_approval_only_without_a_kurus_difference(string row,
        bool pieceLine, string unitPrice, string discountPercent, string quantityKg, int? pieces, string manualCost, bool proceeds)
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(
            new PurchaseOrderLinePriceRequest(unitPrice, pieceLine ? PieceUnitId : null, DiscountPercent: discountPercent),
            averageReadable: false, pieceLine: pieceLine, tracksPieces: pieceLine);
        request = request with { QuantityKg = quantityKg, PieceCount = pieces, SupplierPieceQuantity = pieces, ManualUnitCostTryPerKg = manualCost };
        var exits = new ReturnExitHost();
        var service = world.Service(returns: returns, returnLineage: lineage, returnExits: exits);

        if (!proceeds)
        {
            var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
                service.CreateSupplierReturnAsync(order, request, Metadata("golden-ek1-manual")));
            Assert.Equal((row, 409, PurchaseSupplierReturnCodesV1.QuantityManualCostDifference, 0, 0),
                (row, refusal.Status, refusal.Code, returns.NewWrites, exits.Exits.Count));
            return;
        }

        var pending = await service.CreateSupplierReturnAsync(order, request, Metadata("golden-ek1-manual"));
        // E11 (SAT-D408): the person who entered the return cannot approve its manual cost.
        var same = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("golden-ek1-self")));
        var approved = await world.AsApprover(() =>
            service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("golden-ek1-approve")));

        Assert.Equal((row, "PendingCostException", 409, "Recorded"), (row, pending.Return.Status, same.Status, approved.Return.Status));
        Assert.Equal(("20.475", true, 1638m), (Assert.Single(exits.Exits).UnitCostTryPerKg, exits.Exits[0].ApprovedCostException,
            exits.Exits[0].SupplierReturnAmountTry));
    }

    [Fact]
    public async Task Golden_EK1_E10_a_manual_cost_is_refused_while_the_average_is_readable()
    {
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(
            new PurchaseOrderLinePriceRequest("21.00", DiscountPercent: "2.5"), averageReadable: true);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnExits: new ReturnExitHost()).CreateSupplierReturnAsync(order, request with { ManualUnitCostTryPerKg = "22.1" }, Metadata("golden-ek1-e10")));

        Assert.Equal((409, PurchaseSupplierReturnCodesV1.QuantityAverageAvailable, 0), (refusal.Status, refusal.Code, returns.NewWrites));
    }

    [Fact]
    public async Task Golden_EK1_E15_entered_pieces_that_differ_from_the_labels_pieces_are_refused()
    {
        // 3 pieces entered for the document, 5 pieces leave the label (SAT-D459).
        var (world, order, returns, request, lineage) = await QuantityWorldAsync(
            new PurchaseOrderLinePriceRequest("265.13", PieceUnitId, DiscountPercent: "2.5"), averageReadable: true, pieceLine: true, tracksPieces: true);
        var exits = new ReturnExitHost { StagedAverage = "21.25" };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnLineage: lineage,
            returnExits: exits).CreateSupplierReturnAsync(order, request with { PieceCount = 5, SupplierPieceQuantity = 3 }, Metadata("golden-ek1-e15")));

        Assert.Equal((422, PurchaseSupplierReturnCodesV1.QuantityPieceQuantityMismatch, 0, 0),
            (refusal.Status, refusal.Code, returns.NewWrites, exits.Exits.Count));
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
        QuantityWorldAsync(PurchaseOrderLinePriceRequest price, bool averageReadable, string openKg = "300", bool pieceLine = false,
            bool tracksPieces = false)
    {
        var world = pieceLine
            ? new World { TheoreticalKilograms = new FakeTheoreticalKilograms { Answer = PurchaseTheoreticalKilogramsPerUnitAnswerV1.Found(20m, "product.kg-per-unit.v1/sentetik") } }
            : new World();
        var priced = PricedOrderOf("300", price);
        if (pieceLine) priced = priced with { Lines = [priced.Lines[0] with { UnitId = PieceUnitId }] };
        var order = await world.Service().CreateOrderAsync(priced, Metadata("quantity-order"));
        await world.AsApprover(() => world.Service().ApproveOrderAsync(order.Detail.Order.Id, new("2"), Metadata("quantity-order-approve")));
        var returns = new ReturnStoreFake(order.Detail.Lines[0].LineId);
        var request = LineageReturnRequest("80") with { ManualUnitCostTryPerKg = null };
        var foreign = Guid.NewGuid();
        returns.Suppliers[foreign] = OtherSupplier;
        // Golden I11: the label's lineage is another supplier's receipt; the chosen receipt R5 of this order has 300 kg open.
        var lineage = new LineageFake { AverageCostReadable = averageReadable, TracksPieces = tracksPieces, Roots = [Root(foreign, "1", "1")],
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
        /// <summary>The moving average the Inventory owner would answer for an amount-measured quantity return (SAT-D456).</summary>
        public string? StagedAverage { get; init; }

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
            return Task.FromResult(new PurchaseSupplierReturnExitResultV1(request.InventoryMovementId, LineageLabel, request.QuantityKg, false,
                request.SupplierReturnAmountTry is not null && request.UnitCostTryPerKg.Length == 0 ? StagedAverage : request.UnitCostTryPerKg));
        }
    }
}
