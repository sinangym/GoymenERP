using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>AK-8: the Inventory gate and the lineage composition (SAT-D433 TI1, SAT-D441, SAT-D445).</summary>
public sealed class InventorySupplierReturnLineageTests
{
    private static readonly Guid Company = Id(900);
    private static readonly Guid Product = Id(901);
    private static readonly Guid R1Label = Id(1), R2Label = Id(2), Merged = Id(3), Left = Id(4), Right = Id(5), Made = Id(6), Mixed = Id(7);

    [Fact]
    public void With_the_golden_gate_closed_a_foreign_source_label_keeps_the_earlier_503_receipt_trace_refusal()
    {
        var refused = Assert.Throws<InventorySupplierReturnExitException>(() => InventorySupplierReturnLineageGateV1.Admit(
            Left, InventorySupplierReturnLineageModeV1.Lineage, R1Label, acceptedOverrideForTests: false));

        Assert.Equal(503, refused.Status);
        Assert.Equal(InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid, refused.Code);
    }

    [Fact]
    public void With_the_gate_closed_or_open_a_request_without_a_source_label_is_the_unchanged_receipt_return()
    {
        Assert.False(InventorySupplierReturnLineageGateV1.Admit(null, null, R1Label, acceptedOverrideForTests: false));
        Assert.False(InventorySupplierReturnLineageGateV1.Admit(null, null, R1Label, acceptedOverrideForTests: true));
        Assert.False(InventorySupplierReturnLineageGateV1.Admit(R1Label, null, R1Label, acceptedOverrideForTests: true));
    }

    [Fact]
    public void With_the_gate_open_a_foreign_source_label_needs_a_mode_and_a_mode_needs_a_foreign_source_label()
    {
        Assert.True(InventorySupplierReturnLineageGateV1.Admit(Left, InventorySupplierReturnLineageModeV1.Quantity, R1Label, true));
        Assert.Equal(400, Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnLineageGateV1.Admit(Left, null, R1Label, true)).Status);
        Assert.Equal(400, Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnLineageGateV1.Admit(null, InventorySupplierReturnLineageModeV1.Lineage, R1Label, true)).Status);
        Assert.Equal(400, Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnLineageGateV1.Admit(R1Label, InventorySupplierReturnLineageModeV1.Lineage, R1Label, true)).Status);
    }

    [Fact]
    public void A_split_child_of_one_receipt_is_a_single_root_purchase_lineage_with_share_one()
    {
        var lineage = InventorySupplierReturnLineageReader.Compose(Company, Left, Product, [R1Label], new HashSet<Guid>(),
            [(Id(10), InventoryLabelTransformationKind.Split)],
            [new(Id(10), 1, R1Label, 600m)], [new(Id(10), 1, Left, 250m), new(Id(10), 2, Right, 350m)],
            [Receipt(R1Label, Id(101), Id(201), 1)]);

        var root = Assert.Single(lineage.Roots);
        Assert.Equal(InventorySupplierReturnLineageRootKindV1.PurchaseReceipt, root.Kind);
        Assert.Equal(Id(201), root.GoodsAcceptanceId);
        Assert.Equal(new InventoryExactCostRatioV1("1", "1"), root.Share);
        InventorySupplierReturnLineageGateV1.RequireLineageMode(lineage, R1Label);
    }

    [Fact]
    public void A_merge_of_two_receipts_has_kilogram_weighted_shares_and_a_single_exit_naming_it_is_a_request_fault()
    {
        var lineage = InventorySupplierReturnLineageReader.Compose(Company, Merged, Product, [R1Label, R2Label], new HashSet<Guid>(),
            [(Id(10), InventoryLabelTransformationKind.Merge)],
            [new(Id(10), 1, R1Label, 600m), new(Id(10), 2, R2Label, 400m)], [new(Id(10), 1, Merged, 1000m)],
            [Receipt(R1Label, Id(101), Id(201), 1), Receipt(R2Label, Id(102), Id(202), 2)]);

        Assert.Equal(new[] { R1Label, R2Label }, lineage.Roots.Select(root => root.RootLabelId).ToArray());
        Assert.Equal(new InventoryExactCostRatioV1("3", "5"), lineage.Roots[0].Share);
        Assert.Equal(new InventoryExactCostRatioV1("2", "5"), lineage.Roots[1].Share);
        var refused = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnLineageGateV1.RequireLineageMode(lineage, R1Label));
        // AK-8b: several receipts are ONE atomic return with one exit per receipt (StageLineageAsync), never a single exit.
        Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidRequest), (refused.Status, refused.Code));
    }

    [Fact]
    public void A_merge_with_a_manufactured_label_is_a_mixed_lineage_and_refuses_lineage_mode_as_a_mismatch()
    {
        var lineage = InventorySupplierReturnLineageReader.Compose(Company, Mixed, Product, [R1Label, Made], new HashSet<Guid> { Made },
            [(Id(10), InventoryLabelTransformationKind.Merge)],
            [new(Id(10), 1, R1Label, 100m), new(Id(10), 2, Made, 100m)], [new(Id(10), 1, Mixed, 200m)],
            [Receipt(R1Label, Id(101), Id(201), 1)]);

        Assert.Equal(InventorySupplierReturnLineageRootKindV1.PurchaseReceipt, lineage.Roots[0].Kind);
        Assert.Equal(InventorySupplierReturnLineageRootKindV1.Other, lineage.Roots[1].Kind);
        Assert.Equal(new InventoryExactCostRatioV1("1", "2"), lineage.Roots[1].Share);
        var refused = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnLineageGateV1.RequireLineageMode(lineage, R1Label));
        Assert.Equal(409, refused.Status);
        Assert.Equal(InventorySupplierReturnExitCodesV1.LineageMismatch, refused.Code);
    }

    [Fact]
    public void A_root_with_two_receipt_movements_or_a_line_grain_receipt_is_unreadable_and_never_guessed()
    {
        var twice = Assert.Throws<InventorySupplierReturnExitException>(() => InventorySupplierReturnLineageReader.Compose(
            Company, R1Label, Product, [R1Label], new HashSet<Guid>(), [], [], [],
            [Receipt(R1Label, Id(101), Id(201), 1), Receipt(R1Label, Id(102), Id(202), 2)]));
        var lineGrain = Assert.Throws<InventorySupplierReturnExitException>(() => InventorySupplierReturnLineageReader.Compose(
            Company, R1Label, Product, [R1Label], new HashSet<Guid>(), [], [], [],
            [Receipt(R1Label, Id(101), Id(201), 1) with { SourceGrain = (int)StockSourceGrain.Line, SourceLineId = Id(301) }]));

        Assert.Equal(InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid, twice.Code);
        Assert.Equal(InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid, lineGrain.Code);
    }

    [Fact]
    public void An_opening_or_customer_return_root_without_a_goods_receipt_is_an_other_root()
    {
        var lineage = InventorySupplierReturnLineageReader.Compose(Company, R1Label, Product, [R1Label], new HashSet<Guid>(),
            [], [], [], []);

        var root = Assert.Single(lineage.Roots);
        Assert.Equal(InventorySupplierReturnLineageRootKindV1.Other, root.Kind);
        Assert.Null(root.GoodsAcceptanceId);
    }

    [Fact]
    public void The_lineage_mismatch_code_is_a_closed_409_of_the_exit_contract()
    {
        Assert.Contains(InventorySupplierReturnExitCodesV1.LineageMismatch, InventorySupplierReturnExitCodesV1.Status409Codes);
        Assert.True(InventorySupplierReturnExitCodesV1.IsDefined(409, InventorySupplierReturnExitCodesV1.LineageMismatch));
    }

    private static InventorySupplierReturnLineageReceiptFact Receipt(Guid label, Guid movement, Guid acceptance, int minute) =>
        new(label, movement, acceptance, (int)StockSourceGrain.Whole, null, new DateTimeOffset(2026, 11, 1, 8, minute, 0, TimeSpan.Zero));

    private static Guid Id(int value) => new($"00000000-0000-0000-0000-{value:D12}");
}
