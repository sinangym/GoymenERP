using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D94 (SINAN-CEVAP 34): the receipt host turns Inventory's F017-c conversion answer into receipt warnings; KLN-D110 (2)
/// (ALC-SC34): after a link failure it releases every still Active allocation of the line through Inventory's release port. Synthetic host
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
    // ---- KLN-D110 (2): every still Active allocation on the received line is released too ----------------------------------

    private static readonly Guid GeneralLine = Guid.NewGuid();

    private static readonly InventoryCommandMetadata SweepMetadata = new("SENTETIK-aktor", "SENTETIK-izin", [],
        "SENTETIK-korelasyon", "SENTETIK-neden", "SENTETIK-anahtar", "SENTETIK-kanal");

    private static InventoryFutureAllocationViewV1 ActiveView(Guid id, string remaining, string version = "3", Guid? line = null,
        string status = "Active") => new(id, Guid.NewGuid(), version, status, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        line ?? GeneralLine, Guid.NewGuid(), "SENTETIK-v1", Guid.NewGuid(), remaining, remaining, "0", "0", null, "SENTETIK",
        null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);

    [Fact]
    public async Task After_a_link_failure_every_still_active_allocation_of_the_line_is_released_whole_with_the_failure_text()
    {
        var first = ActiveView(Guid.NewGuid(), "4", version: "2");
        var second = ActiveView(Guid.NewGuid(), "1.25", version: "7");
        var session = new SweepSession([first, second]);

        var releases = await PurchaseGoodsReceiptHost.Participants.ReleaseRemainingActiveAsync(session, GeneralLine,
            InventoryFutureSupplyLinkFailuresV1.DemandNotApproved, SweepMetadata, CancellationToken.None);

        Assert.Equal(new[]
            {
                new PurchaseReceiptFutureSupplyReleaseV1(first.AllocationId, "4", PurchaseReceiptFutureSupplyReleaseCodesV1.DemandNotApproved),
                new PurchaseReceiptFutureSupplyReleaseV1(second.AllocationId, "1.25", PurchaseReceiptFutureSupplyReleaseCodesV1.DemandNotApproved),
            }, releases.ToArray());
        Assert.Equal(new[] { GeneralLine }, session.ListedLines.ToArray());
        Assert.Equal(2, session.Released.Count);
        Assert.All(session.Released, request =>
        {
            Assert.Null(request.QuantityText);
            Assert.Equal(InventoryFutureAllocationReleaseReasonsV1.AcceptanceLinkFailed, request.ReasonCode);
            Assert.Equal(InventoryFutureSupplyLinkFailuresV1.ReleaseText(InventoryFutureSupplyLinkFailuresV1.DemandNotApproved),
                request.ReasonText);
            Assert.Equal(InventoryFutureAllocationOriginsV1.Purchasing, request.Origin);
        });
        Assert.Equal(new[] { 2L, 7L }, session.Released.Select(request => request.ExpectedAllocationVersion).ToArray());
        Assert.All(session.Metadata, metadata => Assert.Same(SweepMetadata, metadata));
    }

    [Fact]
    public async Task A_line_with_no_other_active_allocation_releases_nothing_more()
    {
        var session = new SweepSession([]);

        var releases = await PurchaseGoodsReceiptHost.Participants.ReleaseRemainingActiveAsync(session, GeneralLine,
            InventoryFutureSupplyLinkFailuresV1.LabelBlocked, SweepMetadata, CancellationToken.None);

        Assert.Empty(releases);
        Assert.Empty(session.Released);
    }

    [Theory]
    [InlineData("other-line")]
    [InlineData("not-active")]
    [InlineData("zero-remaining")]
    [InlineData("bad-version")]
    [InlineData("duplicate")]
    [InlineData("not-released-after")]
    [InlineData("unknown-code")]
    public async Task An_inconsistent_sweep_answer_fails_closed_so_the_receipt_rolls_back(string fault)
    {
        var id = Guid.NewGuid();
        var code = fault == "unknown-code" ? "acceptance-link-failed" : InventoryFutureSupplyLinkFailuresV1.ProductMismatch;
        IReadOnlyList<InventoryFutureAllocationViewV1> active = fault switch
        {
            "other-line" => [ActiveView(id, "1", line: Guid.NewGuid())],
            "not-active" => [ActiveView(id, "1", status: "Released")],
            "zero-remaining" => [ActiveView(id, "0")],
            "bad-version" => [ActiveView(id, "1", version: "x")],
            "duplicate" => [ActiveView(id, "1"), ActiveView(id, "1")],
            _ => [ActiveView(id, "1")],
        };
        var session = new SweepSession(active) { LeaveActive = fault == "not-released-after" };

        var refusal = await Assert.ThrowsAsync<InventoryGoodsReceiptException>(() =>
            PurchaseGoodsReceiptHost.Participants.ReleaseRemainingActiveAsync(session, GeneralLine, code, SweepMetadata,
                CancellationToken.None));

        Assert.Equal((503, InventoryGoodsReceiptCodesV1.Unavailable), (refusal.Status, refusal.Code));
    }

    private sealed class SweepSession(IReadOnlyList<InventoryFutureAllocationViewV1> active) : IInventoryFutureAllocationReleaseSessionV1
    {
        public bool LeaveActive { get; init; }
        public List<Guid> ListedLines { get; } = [];
        public List<InventoryFutureAllocationReleaseRequestV1> Released { get; } = [];
        public List<InventoryCommandMetadata> Metadata { get; } = [];

        public Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByPurchaseLineAsync(Guid purchaseOrderLineId,
            CancellationToken cancellationToken = default)
        {
            ListedLines.Add(purchaseOrderLineId);
            return Task.FromResult(active);
        }

        public Task<IReadOnlyList<InventoryFutureAllocationViewV1>> ListActiveByDemandLineAsync(Guid salesOrderLineId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<InventoryFutureAllocationViewV1> ReleaseAsync(InventoryFutureAllocationReleaseRequestV1 request,
            InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
        {
            Released.Add(request);
            Metadata.Add(metadata);
            var view = active.First(item => item.AllocationId == request.AllocationId);
            return Task.FromResult(view with
            {
                Status = LeaveActive ? view.Status : "Released",
                RemainingQuantity = "0",
                ReleasedQuantity = view.RemainingQuantity,
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
