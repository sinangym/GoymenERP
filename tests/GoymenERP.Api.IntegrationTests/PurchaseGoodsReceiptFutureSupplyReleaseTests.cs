using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D94 (SINAN-CEVAP 34): the receipt host turns Inventory's F017-c conversion answer into receipt warnings. Synthetic host
/// evidence only; the release itself (whole remainder, audit, released outbox) is Inventory's and is covered by its own tests.
/// </summary>
public sealed class PurchaseGoodsReceiptFutureSupplyReleaseTests
{
    private static readonly Guid Acceptance = Guid.NewGuid();

    [Fact]
    public void A_link_failed_conversion_becomes_a_release_warning_with_the_whole_released_remainder_and_its_code()
    {
        var released = Guid.NewGuid();
        var converted = Guid.NewGuid();
        var answer = new InventoryGoodsReceiptFutureSupplyResultV1(Acceptance,
        [
            new(converted, InventoryFutureSupplyConversionOutcomeV1.Converted.ToString(), "2", Guid.NewGuid(), "1.9", null),
            new(released, InventoryFutureSupplyConversionOutcomeV1.LinkFailed.ToString(), "7.5", null, null,
                InventoryFutureSupplyLinkFailuresV1.DemandNotApproved),
        ], false);

        var releases = PurchaseGoodsReceiptHost.Participants.LinkFailedReleases(answer, Acceptance);

        var only = Assert.Single(releases);
        Assert.Equal(new PurchaseReceiptFutureSupplyReleaseV1(released, "7.5",
            PurchaseReceiptFutureSupplyReleaseCodesV1.DemandNotApproved), only);
    }

    [Fact]
    public void A_receipt_without_future_allocations_or_with_only_conversions_has_no_release_warning()
    {
        Assert.Empty(PurchaseGoodsReceiptHost.Participants.LinkFailedReleases(
            new InventoryGoodsReceiptFutureSupplyResultV1(Acceptance, [], false), Acceptance));
        Assert.Empty(PurchaseGoodsReceiptHost.Participants.LinkFailedReleases(
            new InventoryGoodsReceiptFutureSupplyResultV1(Acceptance,
                [new(Guid.NewGuid(), InventoryFutureSupplyConversionOutcomeV1.Converted.ToString(), "1", Guid.NewGuid(), "1", null)],
                true), Acceptance));
    }

    [Fact]
    public void A_replayed_conversion_answer_returns_the_same_stored_release_warnings()
    {
        var released = Guid.NewGuid();
        var answer = new InventoryGoodsReceiptFutureSupplyResultV1(Acceptance,
            [new(released, InventoryFutureSupplyConversionOutcomeV1.LinkFailed.ToString(), "3", null, null,
                InventoryFutureSupplyLinkFailuresV1.LabelBlocked)], true);

        var releases = PurchaseGoodsReceiptHost.Participants.LinkFailedReleases(answer, Acceptance);

        Assert.Equal(released, Assert.Single(releases).FutureAllocationId);
        Assert.Equal(PurchaseReceiptFutureSupplyReleaseCodesV1.LabelBlocked, releases[0].FailureCode);
    }

    [Theory]
    [InlineData("other-acceptance")]
    [InlineData("unknown-outcome")]
    [InlineData("link-failed-without-code")]
    [InlineData("null-answer")]
    public void An_inconsistent_conversion_answer_fails_closed_instead_of_hiding_a_release(string fault)
    {
        var answer = fault switch
        {
            "other-acceptance" => new InventoryGoodsReceiptFutureSupplyResultV1(Guid.NewGuid(), [], false),
            "unknown-outcome" => new InventoryGoodsReceiptFutureSupplyResultV1(Acceptance,
                [new(Guid.NewGuid(), "Skipped", "1", null, null, null)], false),
            "link-failed-without-code" => new InventoryGoodsReceiptFutureSupplyResultV1(Acceptance,
                [new(Guid.NewGuid(), InventoryFutureSupplyConversionOutcomeV1.LinkFailed.ToString(), "1", null, null, " ")], false),
            "null-answer" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        var refusal = Assert.Throws<InventoryGoodsReceiptException>(() =>
            PurchaseGoodsReceiptHost.Participants.LinkFailedReleases(answer, Acceptance));

        Assert.Equal((503, InventoryGoodsReceiptCodesV1.Unavailable), (refusal.Status, refusal.Code));
    }

    [Theory]
    [InlineData(InventoryFutureSupplyLinkFailuresV1.LabelBlocked)]
    [InlineData(InventoryFutureSupplyLinkFailuresV1.UnitConversionUnavailable)]
    [InlineData(InventoryFutureSupplyLinkFailuresV1.DemandNotApproved)]
    [InlineData(InventoryFutureSupplyLinkFailuresV1.ProductMismatch)]
    [InlineData(InventoryFutureSupplyLinkFailuresV1.InsufficientLabelQuantity)]
    [InlineData(InventoryFutureSupplyLinkFailuresV1.OwnerNotCompany)]
    public void Every_Inventory_link_failure_code_is_a_known_Purchasing_release_code(string failure)
    {
        // The Purchasing service fails closed on an unknown code; this keeps the two owners' lists in step.
        Assert.Contains(failure, PurchaseReceiptFutureSupplyReleaseCodesV1.All);
    }
}
