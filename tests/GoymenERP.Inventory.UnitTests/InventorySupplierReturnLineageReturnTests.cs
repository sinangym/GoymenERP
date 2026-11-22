using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// AK-8b (SAT-D433 steps 2–3, SAT-D440, SAT-D442, K3-B2, TI3): the atomic multi-receipt return as Inventory owns it — the
/// distribution a request must match, and the invoiced / invoice-pending parts left of a receipt with the preparer's explicit
/// choice between them. Golden rows of <c>ADET_KG_FORMUL_GOLDEN_V1.md</c> section 4 are named in the facts that pin them.
/// </summary>
public sealed class InventorySupplierReturnLineageReturnTests
{
    private static readonly Guid Company = Id(900), Product = Id(901), Label = Id(902), Return = Id(903);
    private static readonly Guid R1 = Id(1), R2 = Id(2), R3 = Id(3), A1 = Id(11), A2 = Id(12), A3 = Id(13);
    private static readonly Guid InvoiceA = Id(21), InvoiceB = Id(22), UndoA = Id(31);

    [Theory]
    [InlineData("I01", "300", null, "3/5,2/5", "180,120", null)]
    [InlineData("I02", "250", null, "3/5,2/5", "150,100", null)]
    [InlineData("I03", "100", null, "21/50,7/25,3/10", "42,28,30", null)]
    [InlineData("I09", "100", null, "1/3,2/3", "33.333,66.667", null)]
    [InlineData("I10", "100", 10, "21/50,7/25,3/10", "42,28,30", "5,2,3")]
    [InlineData("I15", "50", 5, "1/2,1/2", "25,25", "3,2")]
    public void A_request_that_names_the_owned_distribution_is_admitted_and_staged_fewest_pieces_first(string row, string totalKg, int? pieces,
        string shares, string kilograms, string? counts)
    {
        var lineage = Lineage(shares);
        var kg = kilograms.Split(',');
        var count = counts?.Split(',').Select(int.Parse).ToArray();
        var request = Request(totalKg, pieces, lineage.Roots.Select((root, index) => Part(root.GoodsAcceptanceId!.Value, kg[index], count?[index])).ToArray());

        var ordered = InventorySupplierReturnLineageDistributionV1.Match(lineage, decimal.Parse(totalKg, System.Globalization.CultureInfo.InvariantCulture), request);

        Assert.Equal(request.Parts.Count, ordered.Count);
        Assert.Equal(ordered.Select(part => part.PieceCount ?? 0).Order(), ordered.Select(part => part.PieceCount ?? 0));
        Assert.Equal(totalKg, InventorySupplierReturnLineageExitContractV1.Validate(request, Metadata(), Company)
            .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotNull(row);
    }

    [Theory]
    [InlineData("179.999,120.001")]
    [InlineData("300")]
    [InlineData("180,120,0")]
    public void A_request_that_moves_kilograms_between_receipts_or_drops_a_receipt_is_the_409_lineage_mismatch(string kilograms)
    {
        var lineage = Lineage("3/5,2/5");
        var acceptances = new[] { A1, A2, A3 };
        var request = Request("300", null, kilograms.Split(',').Select((kg, index) => Part(acceptances[index], kg, null)).ToArray());

        var refused = Assert.Throws<InventorySupplierReturnExitException>(() => InventorySupplierReturnLineageDistributionV1.Match(lineage, 300m, request));

        Assert.Equal((409, InventorySupplierReturnExitCodesV1.LineageMismatch), (refused.Status, refused.Code));
    }

    [Fact]
    public void A_piece_return_over_several_receipts_with_the_SAT_D442_gate_closed_keeps_the_503_boundary_and_one_receipt_does_not_need_it()
    {
        var lineage = Lineage("21/50,7/25,3/10");
        var request = Request("100", 10, [Part(A1, "42", 5), Part(A2, "28", 2), Part(A3, "30", 3)]);
        var single = Lineage("1/1");

        var refused = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnLineageDistributionV1.Match(lineage, 100m, request, pieceGoldenOverrideForTests: false));
        var one = InventorySupplierReturnLineageDistributionV1.Match(single, 100m, Request("100", 10, [Part(A1, "100", 10)]),
            pieceGoldenOverrideForTests: false);

        Assert.Equal((503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid), (refused.Status, refused.Code));
        Assert.Single(one);
    }

    [Fact]
    public void A_lineage_with_a_non_purchase_root_is_never_distributed_and_duplicate_or_unbalanced_parts_are_a_request_fault()
    {
        var mixed = new InventorySupplierReturnLineageV1(Company, Label, Product,
            [Root(R1, A1, 1, "1/2"), new(R2, InventorySupplierReturnLineageRootKindV1.Other, null, null, null, Ratio("1/2"))]);

        var mismatch = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnLineageDistributionV1.Match(mixed, 100m, Request("100", null, [Part(A1, "100", null)])));
        var duplicate = Assert.Throws<InventorySupplierReturnExitException>(() => InventorySupplierReturnLineageExitContractV1.Validate(
            Request("300", null, [Part(A1, "180", null), Part(A1, "120", null)]), Metadata(), Company));
        var pieces = Assert.Throws<InventorySupplierReturnExitException>(() => InventorySupplierReturnLineageExitContractV1.Validate(
            Request("300", 10, [Part(A1, "180", 5), Part(A2, "120", null)]), Metadata(), Company));

        Assert.Equal((409, InventorySupplierReturnExitCodesV1.LineageMismatch), (mismatch.Status, mismatch.Code));
        Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidRequest), (duplicate.Status, duplicate.Code));
        Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidRequest), (pieces.Status, pieces.Code));
    }

    [Fact]
    public void I04_the_preparer_chooses_100_invoiced_and_80_pending_of_a_receipt_with_240_invoiced_and_360_pending_left()
    {
        var state = SupplierReturnReceiptCohorts.Resolve(600m, [Effect(InvoiceA, 240m)], [])!;

        var chosen = SupplierReturnReceiptCohorts.Choose(state, 180m, [new(null, ExactCostRatio.Of(80m)), new(InvoiceA, ExactCostRatio.Of(100m))]);
        var none = SupplierReturnReceiptCohorts.Choose(state, 180m, null);
        var after = SupplierReturnReceiptCohorts.Resolve(600m, [Effect(InvoiceA, 240m)], [Exit(Id(41), 180m, chosen.Selected)])!;

        Assert.Equal(("360", "240", "600"), (Text(state.PendingKg), Text(state.InvoicedKg[InvoiceA]), Text(state.OpenKg)));
        Assert.Equal(SupplierReturnCohortChoiceOutcome.Chosen, chosen.Outcome);
        // K3-B2: with an invoiced and a pending part both open the system never assigns the part itself.
        Assert.Equal(SupplierReturnCohortChoiceOutcome.ChoiceRequired, none.Outcome);
        Assert.Equal(("280", "140", "420"), (Text(after.PendingKg), Text(after.InvoicedKg[InvoiceA]), Text(after.OpenKg)));
    }

    [Theory]
    [InlineData("0", "250", "LimitExceeded")]
    [InlineData("361", "0", "Mismatch")]
    [InlineData("100", "79", "Mismatch")]
    [InlineData("180", "0", "Chosen")]
    public void TI3_a_choice_never_takes_more_than_what_is_left_of_a_part_and_must_add_up_to_the_share(string pending, string invoiced, string outcome)
    {
        var state = SupplierReturnReceiptCohorts.Resolve(600m, [Effect(InvoiceA, 240m)], [])!;
        var quantity = invoiced == "250" ? 250m : 180m;

        var choice = SupplierReturnReceiptCohorts.Choose(state, quantity, [new(null, ExactCostRatio.Of(decimal.Parse(pending))),
            new(InvoiceA, ExactCostRatio.Of(decimal.Parse(invoiced)))]);

        Assert.Equal(outcome, choice.Outcome.ToString());
    }

    [Fact]
    public void I07_a_share_above_the_receipts_open_quantity_is_refused_and_an_unknown_invoice_is_a_mismatch()
    {
        // 500 of R1's 600 kg already left with earlier returns: 100 kg is open, the 180 kg share is refused, never moved.
        var state = SupplierReturnReceiptCohorts.Resolve(600m, [], [Exit(Id(41), 500m, null)])!;

        Assert.Equal("100", Text(state.OpenKg));
        Assert.Equal(SupplierReturnCohortChoiceOutcome.LimitExceeded, SupplierReturnReceiptCohorts.Choose(state, 180m, null).Outcome);
        Assert.Equal(SupplierReturnCohortChoiceOutcome.Chosen, SupplierReturnReceiptCohorts.Choose(state, 100m, null).Outcome);
        Assert.Equal(SupplierReturnCohortChoiceOutcome.Mismatch,
            SupplierReturnReceiptCohorts.Choose(state, 100m, [new(InvoiceB, ExactCostRatio.Of(100m))]).Outcome);
    }

    [Fact]
    public void A_single_open_part_implies_the_choice_and_several_invoices_need_one()
    {
        var pendingOnly = SupplierReturnReceiptCohorts.Resolve(600m, [], [])!;
        var whole = SupplierReturnReceiptCohorts.Resolve(600m, [new SupplierReturnCohortEffect(InvoiceA, null, [])], [])!;
        var two = SupplierReturnReceiptCohorts.Resolve(600m, [Effect(InvoiceA, 300m), Effect(InvoiceB, 300m)], [])!;

        Assert.Null(Assert.Single(SupplierReturnReceiptCohorts.Choose(pendingOnly, 10m, null).Selected).SourceEventId);
        Assert.Equal(InvoiceA, Assert.Single(SupplierReturnReceiptCohorts.Choose(whole, 10m, null).Selected).SourceEventId);
        Assert.Equal(("0", "600"), (Text(whole.PendingKg), Text(whole.InvoicedKg[InvoiceA])));
        Assert.Equal(SupplierReturnCohortChoiceOutcome.ChoiceRequired, SupplierReturnReceiptCohorts.Choose(two, 10m, null).Outcome);
    }

    [Fact]
    public void A_history_that_cannot_be_attributed_exactly_is_unreadable_and_never_estimated()
    {
        // An earlier exit without a recorded choice on an invoiced receipt: nobody knows which part it took.
        Assert.Null(SupplierReturnReceiptCohorts.Resolve(600m, [Effect(InvoiceA, 240m)], [Exit(Id(41), 50m, null)]));
        Assert.Null(SupplierReturnReceiptCohorts.Resolve(600m, [new SupplierReturnCohortEffect(InvoiceA, null, [])], [Exit(Id(41), 50m, null)]));
        // More returned than received, a choice that does not add up to its exit, and an invoice larger than the receipt.
        Assert.Null(SupplierReturnReceiptCohorts.Resolve(600m, [], [Exit(Id(41), 601m, null)]));
        Assert.Null(SupplierReturnReceiptCohorts.Resolve(600m, [Effect(InvoiceA, 240m)], [Exit(Id(41), 50m, [new(null, ExactCostRatio.Of(49m))])]));
        Assert.Null(SupplierReturnReceiptCohorts.Resolve(600m, [Effect(InvoiceA, 601m)], []));
        Assert.Null(SupplierReturnReceiptCohorts.Resolve(600m, [Effect(InvoiceA, 240m)], [Exit(Id(41), 300m, [new(InvoiceA, ExactCostRatio.Of(300m))])]));
    }

    [Fact]
    public void An_undo_every_invoice_already_counted_reduced_the_receipt_before_the_invoices_were_measured()
    {
        var counted = new SupplierReturnCohortEffect(InvoiceA, ExactCostRatio.Of(240m), [UndoA]);

        var state = SupplierReturnReceiptCohorts.Resolve(600m, [counted], [new SupplierReturnCohortExit(Id(41), UndoA, 100m, null)])!;

        Assert.Equal(("260", "240", "500"), (Text(state.PendingKg), Text(state.InvoicedKg[InvoiceA]), Text(state.OpenKg)));
    }

    [Theory]
    [InlineData("80", "40.2187", "3217.5", "3217.5", "0")]       // 3217.496 → 3217.50: no difference although the unit values differ
    [InlineData("80", "21.25", "1640", "1700", "60")]           // golden I11 shape: 1 700,00 − 1 640,00 = 60,00
    [InlineData("80", "10.2531", "820", "820.25", "0.25")]
    [InlineData("3", "0.335", "1.01", "1.01", "0")]             // 1.005 → half away from zero on the exit side too
    public void SAT_D456_the_difference_is_the_kurus_rounded_exit_amount_minus_the_kurus_rounded_billed_amount(string kg, string average,
        string billed, string exit, string difference)
    {
        static decimal Number(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var answer = SupplierReturnLineageCalculator.QuantityReturnAmountDifference(Number(kg), Number(average), Number(billed));

        Assert.Equal((Number(exit), Number(difference)), answer);
    }

    [Fact]
    public void SAT_D456_the_billed_amount_is_a_canonical_positive_kurus_text_and_is_null_on_every_earlier_request()
    {
        InventorySupplierReturnExitRequestV1 Request(string? amount) => new(Company, Return, Id(51), A1, Id(52), BlockState.Free, "80", null,
            DateTimeOffset.UnixEpoch, SourceLabelId: Label, LineageMode: InventorySupplierReturnLineageModeV1.Quantity, SupplierReturnAmountTry: amount);

        Assert.Null(InventorySupplierReturnExitContractV1.Validate(Request("3217.5"), Metadata(), Company).OriginalUnitCostTryPerKg);
        foreach (var bad in new[] { "0", "3217.50", "3217.505", "-1", "" })
            Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidCost), Refusal(() =>
                InventorySupplierReturnExitContractV1.Validate(Request(bad), Metadata(), Company)));
        Assert.DoesNotContain("SupplierReturnAmountTry", System.Text.Json.JsonSerializer.Serialize(Request(null)), StringComparison.Ordinal);
    }

    [Fact]
    public void SAT_D460_an_approved_stock_exit_amount_needs_the_billed_amount_and_the_approval_flag()
    {
        InventorySupplierReturnExitRequestV1 Request(string? billed, bool? approved, string? approvedAmount) => new(Company, Return, Id(51), A1,
            Id(52), BlockState.Free, "80", null, DateTimeOffset.UnixEpoch, SourceLabelId: Label,
            LineageMode: InventorySupplierReturnLineageModeV1.Quantity, ApprovedCostException: approved, SupplierReturnAmountTry: billed,
            ApprovedStockExitAmountTry: approvedAmount);

        InventorySupplierReturnExitContractV1.Validate(Request("820", true, "820.25"), Metadata(), Company);
        foreach (var bad in new[] { Request(null, true, "820.25"), Request("820", null, "820.25"), Request("820", true, "820.250"),
                     Request("820", true, "0") })
            Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidCost), Refusal(() =>
                InventorySupplierReturnExitContractV1.Validate(bad, Metadata(), Company)));
        Assert.DoesNotContain("ApprovedStockExitAmountTry", System.Text.Json.JsonSerializer.Serialize(Request(null, null, null)), StringComparison.Ordinal);
    }

    private static (int, string) Refusal(Action action)
    {
        var refused = Assert.Throws<InventorySupplierReturnExitException>(action);
        return (refused.Status, refused.Code);
    }

    [Fact]
    public void The_quantity_cost_unreadable_code_is_a_closed_409_and_the_exception_flag_is_part_of_the_request_shape()
    {
        Assert.True(InventorySupplierReturnExitCodesV1.IsDefined(409, InventorySupplierReturnExitCodesV1.QuantityCostUnreadable));
        Assert.False(InventorySupplierReturnExitCodesV1.IsDefined(503, InventorySupplierReturnExitCodesV1.QuantityCostUnreadable));
        var json = System.Text.Json.JsonSerializer.Serialize(new InventorySupplierReturnExitRequestV1(Company, Return, Id(51), A1, Id(52),
            BlockState.Free, "1", null, DateTimeOffset.UnixEpoch));
        // Null on every earlier request: an earlier idempotency key's payload hash does not change.
        Assert.DoesNotContain("LineageSelection", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ApprovedCostException", json, StringComparison.Ordinal);
    }

    private static InventorySupplierReturnLineageV1 Lineage(string shares)
    {
        var roots = new[] { (R1, A1), (R2, A2), (R3, A3) };
        return new InventorySupplierReturnLineageV1(Company, Label, Product,
            shares.Split(',').Select((share, index) => Root(roots[index].Item1, roots[index].Item2, index + 1, share)).ToArray());
    }

    private static InventorySupplierReturnLineageRootV1 Root(Guid label, Guid acceptance, int minute, string share) =>
        new(label, InventorySupplierReturnLineageRootKindV1.PurchaseReceipt, acceptance, Id(100 + minute),
            new DateTimeOffset(2026, 11, 1, 8, minute, 0, TimeSpan.Zero), Ratio(share));

    private static InventoryExactCostRatioV1 Ratio(string text) => new(text.Split('/')[0], text.Split('/')[1]);

    private static InventorySupplierReturnLineageExitRequestV1 Request(string totalKg, int? pieces,
        IReadOnlyList<InventorySupplierReturnLineagePartV1> parts) =>
        new(Company, Return, Label, BlockState.Free, totalKg, pieces, DateTimeOffset.UnixEpoch, parts);

    private static InventorySupplierReturnLineagePartV1 Part(Guid acceptance, string kg, int? pieces) =>
        new(acceptance, Guid.NewGuid(), Guid.NewGuid(), kg, pieces, "19.9936", null);

    private static InventoryCommandMetadata Metadata() =>
        new("SENTETIK-actor", "purchasing.supplier-returns.record", [], "SENTETIK-correlation", null, "SENTETIK-key", "api", null, null);

    private static SupplierReturnCohortEffect Effect(Guid id, decimal kg) => new(id, ExactCostRatio.Of(kg), []);

    private static SupplierReturnCohortExit Exit(Guid movement, decimal kg, IReadOnlyList<SupplierReturnCohortSelection>? selection) =>
        new(movement, null, kg, selection);

    private static string Text(ExactCostRatio value) => value.Round(3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static Guid Id(int value) => new($"00000000-0000-0000-0000-{value:D12}");
}
