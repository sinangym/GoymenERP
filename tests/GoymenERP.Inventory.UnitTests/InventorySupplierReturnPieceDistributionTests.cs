using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D442: the returned pieces of a mixed-lineage label over its acceptances (floor, remainder to the largest share,
/// ties to the first in acceptance order), pinned to ADET_KG_FORMUL_GOLDEN_V1 rows I10 and I15. The gate stays closed.
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
    public void The_distribution_stays_closed_until_its_independent_golden_is_in_main()
    {
        Assert.False(InventorySupplierReturnPieceDistributionV1.GoldenAccepted);
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
