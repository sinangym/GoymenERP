using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// ALC-SC34 (KLN-D94, KLN-D110): the bulk goods receipt answers the future supply allocations its labels released, with the same
/// item and code list as the single receipt. SENTETIK / NOT_ENV; the release itself (whole remainder, audit, released outbox) is
/// Inventory's.
/// </summary>
public sealed class PurchaseGoodsReceiptBatchFutureSupplyReleaseTests
{
    private const string Code = PurchaseReceiptFutureSupplyReleaseCodesV1.DemandNotApproved;

    [Fact]
    public void Each_posted_label_s_released_allocations_are_collected_in_label_order()
    {
        var first = new PurchaseReceiptFutureSupplyReleaseV1(Guid.NewGuid(), "4", Code);
        var second = new PurchaseReceiptFutureSupplyReleaseV1(Guid.NewGuid(), "2.5", PurchaseReceiptFutureSupplyReleaseCodesV1.LabelBlocked);
        var third = new PurchaseReceiptFutureSupplyReleaseV1(Guid.NewGuid(), "1", PurchaseReceiptFutureSupplyReleaseCodesV1.LabelBlocked);
        var batch = new List<PurchaseReceiptFutureSupplyReleaseV1>();

        Assert.True(PurchaseGoodsReceiptBatchFutureSupplyReleasesV1.TryAppend(batch, [first]));
        Assert.True(PurchaseGoodsReceiptBatchFutureSupplyReleasesV1.TryAppend(batch, []));
        Assert.True(PurchaseGoodsReceiptBatchFutureSupplyReleasesV1.TryAppend(batch, [second, third]));

        Assert.Equal(new[] { first, second, third }, batch.ToArray());
    }

    [Fact]
    public void The_same_allocation_released_by_two_labels_is_an_inconsistent_answer_and_nothing_is_appended()
    {
        var id = Guid.NewGuid();
        var other = new PurchaseReceiptFutureSupplyReleaseV1(Guid.NewGuid(), "1", Code);
        var batch = new List<PurchaseReceiptFutureSupplyReleaseV1> { new(id, "3", Code) };

        Assert.False(PurchaseGoodsReceiptBatchFutureSupplyReleasesV1.TryAppend(batch, [other, new(id, "3", Code)]));
        Assert.False(PurchaseGoodsReceiptBatchFutureSupplyReleasesV1.TryAppend(batch, [other, other]));

        Assert.Single(batch);
    }

    [Fact]
    public void A_batch_answer_without_releases_carries_an_empty_list_on_the_wire()
    {
        var response = new PurchaseGoodsReceiptBatchResponseV1(null!, IsReplay: false, "SENTETIK-korelasyon");

        Assert.Empty(response.FutureSupplyReleases);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("futureSupplyReleases").ValueKind);
        Assert.Equal(0, json.RootElement.GetProperty("futureSupplyReleases").GetArrayLength());
    }

    [Fact]
    public void A_batch_answer_carries_the_single_receipt_s_release_item_shape()
    {
        var id = Guid.NewGuid();
        var response = new PurchaseGoodsReceiptBatchResponseV1(null!, IsReplay: false, "SENTETIK-korelasyon")
        {
            FutureSupplyReleases = [new(id, "7.5", PurchaseReceiptFutureSupplyReleaseCodesV1.InsufficientLabelQuantity)],
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var item = Assert.Single(json.RootElement.GetProperty("futureSupplyReleases").EnumerateArray().ToArray());
        Assert.Equal(new[] { "futureAllocationId", "releasedQuantity", "failureCode" }, item.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(id, item.GetProperty("futureAllocationId").GetGuid());
        Assert.Equal("7.5", item.GetProperty("releasedQuantity").GetString());
        Assert.Equal("insufficient-label-quantity", item.GetProperty("failureCode").GetString());
    }
}
