using System.Globalization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D442: the returned pieces of a mixed-lineage label over its acceptances (floor, remainder to the largest share, ties to
/// the first in acceptance order), pinned to ADET_KG_FORMUL_GOLDEN_V1 rows I10 and I15 on the one live calculator
/// (<see cref="SupplierReturnLineageCalculator.DistributeByShare"/>, exact shares; K3-B P3-10 removed the unused kg-weighted copy).
/// SAT-D440: the kilograms are cut to three decimals and the remainder goes to the largest share. The gate is open in this train;
/// the closed behaviour stays pinned through the test parameter.
/// </summary>
public sealed class InventorySupplierReturnPieceDistributionTests
{
    private static readonly Guid R1 = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid R2 = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid R3 = Guid.Parse("a1000000-0000-0000-0000-000000000003");

    private static ExactCostRatio Share(decimal numerator, decimal denominator) => ExactCostRatio.Of(numerator).Divide(denominator);

    [Fact]
    public void Golden_I10_rounds_down_and_gives_the_remaining_piece_to_the_largest_share()
    {
        var parts = SupplierReturnLineageCalculator.DistributeByShare(
            [new(R1, 1, Share(21m, 50m)), new(R2, 2, Share(7m, 25m)), new(R3, 3, Share(3m, 10m))], 100m, 10);

        Assert.Equal([(R1, 5), (R2, 2), (R3, 3)], parts.Select(part => (part.RootId, part.Pieces!.Value)));
        Assert.Equal([42m, 28m, 30m], parts.Select(part => part.QuantityKg));
    }

    [Fact]
    public void Golden_I15_gives_the_remaining_piece_to_the_first_of_equal_largest_shares()
    {
        var parts = SupplierReturnLineageCalculator.DistributeByShare([new(R2, 2, Share(1m, 2m)), new(R1, 1, Share(1m, 2m))], 50m, 5);

        Assert.Equal([(R1, 3, 25m), (R2, 2, 25m)], parts.Select(part => (part.RootId, part.Pieces!.Value, part.QuantityKg)));
    }

    [Fact]
    public void Every_remaining_piece_goes_to_the_largest_share_and_the_total_is_kept()
    {
        var parts = SupplierReturnLineageCalculator.DistributeByShare(
            [new(R1, 1, Share(334m, 1000m)), new(R2, 2, Share(333m, 1000m)), new(R3, 3, Share(333m, 1000m))], 100m, 5);

        Assert.Equal([(R1, 3), (R2, 1), (R3, 1)], parts.Select(part => (part.RootId, part.Pieces!.Value)));
        Assert.Equal(5, parts.Sum(part => part.Pieces!.Value));
    }

    [Fact]
    public void With_three_roots_the_whole_kilogram_remainder_goes_to_the_largest_share_and_the_total_is_kept()
    {
        // SAT-D440 (golden I09): each share is cut to three decimals and the remainder goes to the largest share. With three roots
        // the cut can lose more than 0,001 kg: 2/7, 2/7, 3/7 of 1,000 kg cut to 0,285 / 0,285 / 0,428 leaves 0,002 kg, which the
        // largest share takes (0,430). The rule names no limit on the remainder; whether a remainder above 0,001 kg should be
        // spread otherwise is an open question for Sinan (NEEDS-SINAN), so the decided rule stays as written.
        var parts = SupplierReturnLineageCalculator.DistributeByShare(
            [new(R1, 1, Share(2m, 7m)), new(R2, 2, Share(2m, 7m)), new(R3, 3, Share(3m, 7m))], 1m, null);

        Assert.Equal([(R1, 0.285m), (R2, 0.285m), (R3, 0.430m)], parts.Select(part => (part.RootId, part.QuantityKg)));
        Assert.Equal(1m, parts.Sum(part => part.QuantityKg));
        Assert.All(parts, part => Assert.Null(part.Pieces));
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
    public void The_live_calculator_distributes_pieces_by_the_SAT_D442_rule(int pieces, string shares, string expected)
    {
        // I10 and I15 plus two edges, on the calculator the atomic lineage return stages.
        var ratios = shares.Split(',').Select(text => text.Split('/'))
            .Select(parts => Share(decimal.Parse(parts[0], CultureInfo.InvariantCulture), decimal.Parse(parts[1], CultureInfo.InvariantCulture)))
            .ToArray();
        var ids = ratios.Select((_, index) => new Guid($"a2000000-0000-0000-0000-{index + 1:D12}")).ToArray();

        var live = SupplierReturnLineageCalculator.DistributeByShare(
            ratios.Select((share, index) => new SupplierReturnLineageShare(ids[index], index + 1, share)).ToArray(), 100m, pieces);

        Assert.Equal(expected, string.Join(',', live.Select(part => part.Pieces)));
        Assert.Equal(pieces, live.Sum(part => part.Pieces!.Value));
    }

    [Fact]
    public void Non_positive_pieces_empty_shares_duplicate_roots_and_shares_not_adding_to_one_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SupplierReturnLineageCalculator.DistributeByShare([new(R1, 1, Share(1m, 1m))], 1m, 0));
        Assert.Throws<ArgumentException>(() => SupplierReturnLineageCalculator.DistributeByShare([], 1m, 1));
        Assert.Throws<ArgumentException>(() => SupplierReturnLineageCalculator.DistributeByShare(
            [new(R1, 1, Share(1m, 2m)), new(R1, 2, Share(1m, 2m))], 1m, 1));
        Assert.Throws<ArgumentException>(() => SupplierReturnLineageCalculator.DistributeByShare(
            [new(R1, 1, Share(1m, 2m)), new(R2, 2, Share(1m, 3m))], 1m, 1));
    }
}
