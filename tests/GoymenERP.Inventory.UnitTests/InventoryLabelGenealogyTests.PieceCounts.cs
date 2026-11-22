using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Quantities;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>KLN-D119 (AK-2): split/merge conserve pieces exactly as they conserve kilograms.</summary>
public sealed partial class InventoryLabelGenealogyTests
{
    [Fact]
    public void A_split_of_a_piece_tracking_label_gives_outputs_whose_pieces_add_up_to_the_source()
    {
        var transformation = Transform(InventoryLabelTransformationKind.Split, [Pieces(Source("SRC", 100m), 10)],
            [Pieces(Output("A", 30m), 3), Pieces(Output("B", 70m), 7)]);

        Assert.Equal([3, 7], transformation.Outputs.Select(output => output.Pieces!.Value.Value));
    }

    [Theory]
    [InlineData(3, 6, "inventory_invalid_piece_count")]
    [InlineData(3, 8, "inventory_invalid_piece_count")]
    [InlineData(0, 10, "inventory_piece_count_required")]
    public void A_split_whose_output_pieces_do_not_equal_the_source_pieces_is_refused(int first, int second, string code)
    {
        var error = Assert.Throws<InventoryRuleException>(() => Transform(InventoryLabelTransformationKind.Split,
            [Pieces(Source("SRC", 100m), 10)], [Pieces(Output("A", 30m), first), Pieces(Output("B", 70m), second)]));

        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void A_split_of_a_piece_tracking_label_without_output_pieces_is_refused_fail_closed()
    {
        var error = Assert.Throws<InventoryRuleException>(() => Transform(InventoryLabelTransformationKind.Split,
            [Pieces(Source("SRC", 100m), 10)], [Output("A", 30m), Output("B", 70m)]));

        Assert.Equal(InventoryRuleCodes.PieceCountRequired, error.Code);
    }

    [Fact]
    public void A_piece_less_source_gives_piece_less_outputs_and_mixed_sources_are_refused()
    {
        var plain = Transform(InventoryLabelTransformationKind.Split, [Source("SRC", 100m)], [Output("A", 30m), Output("B", 70m)]);
        Assert.All(plain.Outputs, output => Assert.Null(output.Pieces));

        Assert.Equal(InventoryRuleCodes.PieceCountNotTracked, Assert.Throws<InventoryRuleException>(() => Transform(
            InventoryLabelTransformationKind.Split, [Source("SRC", 100m)], [Pieces(Output("A", 30m), 1), Pieces(Output("B", 70m), 1)])).Code);
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(() => Transform(
            InventoryLabelTransformationKind.Merge, [Pieces(Source("S1", 40m), 4), Source("S2", 60m)], [Pieces(Output("M", 100m), 4)])).Code);
    }

    [Fact]
    public void A_merge_of_piece_tracking_labels_gives_one_output_with_the_sum_of_their_pieces()
    {
        var merge = Transform(InventoryLabelTransformationKind.Merge, [Pieces(Source("S1", 40m), 4), Pieces(Source("S2", 60m), 6)],
            [Pieces(Output("M", 100m), 10)]);

        Assert.Equal(10, merge.Outputs[0].Pieces!.Value.Value);
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(() => Transform(
            InventoryLabelTransformationKind.Merge, [Pieces(Source("S1", 40m), 4), Pieces(Source("S2", 60m), 6)],
            [Pieces(Output("M", 100m), 9)])).Code);
    }

    private static InventoryLabelTransformationSource Pieces(InventoryLabelTransformationSource source, int pieces) =>
        source with { Pieces = PieceCount.Of(pieces) };

    private static InventoryLabelTransformationOutput Pieces(InventoryLabelTransformationOutput output, int pieces) =>
        output with { Pieces = PieceCount.Of(pieces) };
}
