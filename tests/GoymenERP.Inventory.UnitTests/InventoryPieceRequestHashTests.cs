using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// K3-B P3-2 (KLN-D119): the stored payload hash of a supplier-return exit and of a goods-receipt reversal includes the piece
/// count when one is given, so the same idempotency key with different pieces is a payload mismatch; a piece-less request
/// keeps exactly the hash it had before. K3-A P3 (ALS-D124, golden EK1 E26/E28): an explicit-source cohort's piece invoice
/// takes the pending kg rounded to three decimals and the last one the whole pending kg.
/// </summary>
public sealed class InventoryPieceRequestHashTests
{
    private static readonly Guid Company = Guid.Parse("73000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_supplier_return_exits_hash_changes_with_its_pieces_and_a_piece_less_request_keeps_the_earlier_hash()
    {
        var plain = SupplierReturn();

        var three = InventorySupplierReturnExitParticipant.RequestHash(plain with { PieceCount = 3 });
        var four = InventorySupplierReturnExitParticipant.RequestHash(plain with { PieceCount = 4 });
        var none = InventorySupplierReturnExitParticipant.RequestHash(plain);

        Assert.NotEqual(three, four);
        Assert.NotEqual(three, none);
        Assert.Equal(three, InventorySupplierReturnExitParticipant.RequestHash(plain with { PieceCount = 3 }));
        // The kg-only hash is the one stored before the piece count joined it: the request with PurchaseSource and pieces cleared.
        Assert.Equal(none, InventorySupplierReturnExitParticipant.RequestHash(plain with { PieceCount = null }));
        Assert.Equal(64, three.Length);
    }

    [Fact]
    public void A_receipt_reversals_hash_changes_with_its_pieces_and_a_piece_less_request_keeps_the_earlier_hash()
    {
        var plain = new InventoryGoodsReceiptReversalRequestV1(Company, Guid.Parse("73000000-0000-0000-0000-000000000002"),
            Guid.Parse("73000000-0000-0000-0000-000000000003"), Guid.Parse("73000000-0000-0000-0000-000000000004"),
            Guid.Parse("73000000-0000-0000-0000-000000000005"), InventoryGoodsReceiptReversalKindV1.AcceptanceUndo,
            BlockState.Free, "2", null, "SENTETIK", At);

        var one = InventoryGoodsReceiptReversalParticipant.RequestHash(plain with { PieceCount = 1 });
        var two = InventoryGoodsReceiptReversalParticipant.RequestHash(plain with { PieceCount = 2 });
        var none = InventoryGoodsReceiptReversalParticipant.RequestHash(plain);

        Assert.NotEqual(one, two);
        Assert.NotEqual(one, none);
        Assert.Equal(none, InventoryFutureAllocationEngine.PayloadHash(plain with { PurchaseSource = null, PieceCount = null,
            UndoQuantity = null }));
        Assert.Equal(none.ToLowerInvariant(), none);
    }

    [Fact]
    public void Golden_E26_and_E28_an_explicit_source_cohorts_piece_invoice_takes_the_rounded_pending_share_and_the_last_the_rest()
    {
        // E26: 70 pieces / 1 000,530 kg pending; 40 invoiced (not the last) -> 571,731.
        Assert.Equal(0, InventoryPurchaseInvoiceCostStoreV1.CohortPieceShare(true, 1250.4m, ExactCostRatio.Of(1000.53m),
            ExactCostRatio.Of(70m), 40m)!.CompareTo(ExactCostRatio.Of(571.731m)));
        // E28: 3 pieces / 1 000 kg -> 333,333; then 2 / 666,667 -> 333,334 (half away from zero); the last takes 333,333.
        var first = InventoryPurchaseInvoiceCostStoreV1.CohortPieceShare(true, 1000m, ExactCostRatio.Of(1000m), ExactCostRatio.Of(3m), 1m)!;
        var second = InventoryPurchaseInvoiceCostStoreV1.CohortPieceShare(true, 1000m, ExactCostRatio.Of(1000m).Subtract(first),
            ExactCostRatio.Of(2m), 1m)!;
        var last = InventoryPurchaseInvoiceCostStoreV1.CohortPieceShare(true, 1000m, ExactCostRatio.Of(1000m).Subtract(first).Subtract(second),
            ExactCostRatio.Of(1m), 1m)!;
        Assert.Equal([333.333m, 333.334m, 333.333m], new[] { first, second, last }.Select(share => share.Round(6)));
        Assert.Equal(0, first.Add(second).Add(last).CompareTo(ExactCostRatio.Of(1000m)));
    }

    [Fact]
    public void A_cohort_share_is_left_to_the_kilogram_rule_on_a_kilogram_line_or_with_the_switch_closed_and_waits_when_nothing_is_pending()
    {
        Assert.Null(InventoryPurchaseInvoiceCostStoreV1.CohortPieceShare(true, null, ExactCostRatio.Of(10m), ExactCostRatio.Of(5m), 1m));
        Assert.Null(InventoryPurchaseInvoiceCostStoreV1.CohortPieceShare(false, 10m, ExactCostRatio.Of(10m), ExactCostRatio.Of(5m), 1m));
        var waiting = Assert.Throws<InvalidOperationException>(() =>
            InventoryPurchaseInvoiceCostStoreV1.CohortPieceShare(true, 10m, ExactCostRatio.Of(10m), ExactCostRatio.Of(2m), 3m));
        Assert.Equal("purchase_invoice_cost_piece_remaining_share_unavailable", waiting.Message);
    }

    private static InventorySupplierReturnExitRequestV1 SupplierReturn() => new(
        Company,
        Guid.Parse("73000000-0000-0000-0000-000000000011"),
        Guid.Parse("73000000-0000-0000-0000-000000000012"),
        Guid.Parse("73000000-0000-0000-0000-000000000013"),
        Guid.Parse("73000000-0000-0000-0000-000000000014"),
        BlockState.Free,
        "12.125",
        "41.5",
        At);
}
