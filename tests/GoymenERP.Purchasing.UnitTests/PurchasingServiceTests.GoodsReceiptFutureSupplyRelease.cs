using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

// KLN-D94 (SINAN-CEVAP 34): a failed label link releases the future allocation's whole remainder in Inventory; the receipt stands
// and the receiving user sees which reservations were dropped. Synthetic owner-protocol evidence only.
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task A_receipt_whose_future_supply_link_failed_still_commits_and_answers_the_released_allocations()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        var released = new PurchaseReceiptFutureSupplyReleaseV1(Guid.NewGuid(), "5",
            PurchaseReceiptFutureSupplyReleaseCodesV1.LabelBlocked);
        runner.FutureSupplyReleases = [released];

        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, ReceiptRequest(), Metadata("receipt-kln-d94"));

        Assert.Equal(released, Assert.Single(result.FutureSupplyReleases));
        Assert.Equal("2", result.Receipt.AcceptedQuantity);
        Assert.Equal(["receipt-read", "purchase", "first-source", "inventory", "fence", "commit"], runner.Events);
    }

    [Fact]
    public async Task A_receipt_without_released_future_allocations_answers_an_empty_release_list()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();

        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, ReceiptRequest(), Metadata("receipt-kln-d94-none"));

        Assert.Empty(result.FutureSupplyReleases);
    }

    [Fact]
    public async Task A_missing_release_list_from_the_host_is_answered_as_nothing_released()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        runner.FutureSupplyReleases = null;

        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, ReceiptRequest(), Metadata("receipt-kln-d94-null"));

        Assert.Empty(result.FutureSupplyReleases);
    }

    [Theory]
    [InlineData("empty-allocation")]
    [InlineData("duplicate-allocation")]
    [InlineData("zero-quantity")]
    [InlineData("unparsable-quantity")]
    [InlineData("unknown-code")]
    public async Task A_malformed_release_list_fails_closed_before_commit(string fault)
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        var id = Guid.NewGuid();
        var code = PurchaseReceiptFutureSupplyReleaseCodesV1.DemandNotApproved;
        runner.FutureSupplyReleases = fault switch
        {
            "empty-allocation" => [new(Guid.Empty, "1", code)],
            "duplicate-allocation" => [new(id, "1", code), new(id, "2", code)],
            "zero-quantity" => [new(id, "0", code)],
            "unparsable-quantity" => [new(id, "bir", code)],
            "unknown-code" => [new(id, "1", "acceptance-link-failed")],
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, ReceiptRequest(), Metadata("receipt-kln-d94-bad")));

        Assert.Equal((503, PurchaseGoodsReceiptCodesV1.SourceInvalid), (refusal.Status, refusal.Code));
        Assert.DoesNotContain("commit", runner.Events);
    }
}
