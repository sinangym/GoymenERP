using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D442: the returned pieces of a mixed-lineage label over its acceptances (floor, remainder to the largest share,
/// ties to the first in acceptance order), pinned to ADET_KG_FORMUL_GOLDEN_V1 rows I10 and I15. The gate is open in this
/// train; the closed behaviour stays pinned through the test parameter.
/// </summary>
public sealed class InventorySupplierReturnPieceDistributionTests
{
    private static readonly Guid R1 = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid R2 = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid R3 = Guid.Parse("a1000000-0000-0000-0000-000000000003");

    [Fact]
    public void Golden_I10_rounds_down_and_gives_the_remaining_piece_to_the_largest_share()
    {
        var allocation = InventorySupplierReturnPieceDistributionV1.Distribute(10,
            [new(R1, 1, 42m), new(R2, 2, 28m), new(R3, 3, 30m)]);

        Assert.Equal([(R1, 5), (R2, 2), (R3, 3)], allocation.Select(item => (item.AcceptanceId, item.Pieces)));
    }

    [Fact]
    public void Golden_I15_gives_the_remaining_piece_to_the_first_of_equal_largest_shares()
    {
        var allocation = InventorySupplierReturnPieceDistributionV1.Distribute(5, [new(R2, 2, 50m), new(R1, 1, 50m)]);

        Assert.Equal([(R2, 2), (R1, 3)], allocation.Select(item => (item.AcceptanceId, item.Pieces)));
    }

    [Fact]
    public void Every_remaining_piece_goes_to_the_largest_share_and_the_total_is_kept()
    {
        var allocation = InventorySupplierReturnPieceDistributionV1.Distribute(5,
            [new(R1, 1, 0.334m), new(R2, 2, 0.333m), new(R3, 3, 0.333m)]);

        Assert.Equal([(R1, 3), (R2, 1), (R3, 1)], allocation.Select(item => (item.AcceptanceId, item.Pieces)));
        Assert.Equal(5, allocation.Sum(item => item.Pieces));
    }

    [Fact]
    public void The_SAT_D442_gate_is_open_in_this_train_and_its_closed_state_stays_pinned_by_the_test_parameter()
    {
        Assert.True(InventorySupplierReturnPieceDistributionV1.GoldenAccepted);
        Assert.True(InventorySupplierReturnPieceDistributionV1.IsAccepted());
        Assert.False(InventorySupplierReturnPieceDistributionV1.IsAccepted(overrideForTests: false));
    }

    [Theory]
    [InlineData(10, "21/50,7/25,3/10", "5,2,3")]
    [InlineData(5, "1/2,1/2", "3,2")]
    [InlineData(1, "1/3,2/3", "0,1")]
    [InlineData(7, "1/1", "7")]
    public void The_live_calculator_distributes_pieces_exactly_like_the_pinned_SAT_D442_rule(int pieces, string shares, string expected)
    {
        // I10 and I15 plus two edges: the calculator staged by the atomic lineage return and this pinned rule never differ.
        var ratios = shares.Split(',').Select(text => text.Split('/')).Select(parts =>
            GoymenERP.Modules.Inventory.Domain.Costing.ExactCostRatio.Of(decimal.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture))
                .Divide(decimal.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var ids = ratios.Select((_, index) => new Guid($"a2000000-0000-0000-0000-{index + 1:D12}")).ToArray();

        var live = GoymenERP.Modules.Inventory.Domain.Costing.SupplierReturnLineageCalculator.DistributeByShare(
            ratios.Select((share, index) => new GoymenERP.Modules.Inventory.Domain.Costing.SupplierReturnLineageShare(ids[index], index + 1, share)).ToArray(),
            100m, pieces);
        var pinned = InventorySupplierReturnPieceDistributionV1.Distribute(pieces,
            ratios.Select((share, index) => new InventorySupplierReturnPieceShareV1(ids[index], index + 1, share.Multiply(600m).Round(6))).ToArray());

        Assert.Equal(expected, string.Join(',', live.Select(part => part.Pieces)));
        Assert.Equal(live.Select(part => (part.RootId, part.Pieces!.Value)), pinned.Select(part => (part.AcceptanceId, part.Pieces)));
    }

    [Fact]
    public void Negative_pieces_empty_shares_and_duplicate_acceptances_are_refused()
    {
        Assert.Equal(InventoryRuleCodes.InvalidPieceCount, Assert.Throws<InventoryRuleException>(() =>
            InventorySupplierReturnPieceDistributionV1.Distribute(-1, [new(R1, 1, 1m)])).Code);
        Assert.Throws<InventoryRuleException>(() => InventorySupplierReturnPieceDistributionV1.Distribute(1, []));
        Assert.Throws<InventoryRuleException>(() => InventorySupplierReturnPieceDistributionV1.Distribute(1,
            [new(R1, 1, 1m), new(R1, 2, 1m)]));
        Assert.Throws<InventoryRuleException>(() => InventorySupplierReturnPieceDistributionV1.Distribute(1,
            [new(R1, 1, 0m)]));
    }
}
