using System.Data;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// F-017 §3.5 F17-4 (ALS-D83/ALS-D85): the purchasing side of the future allocation link through the service with a fake Inventory
/// participant — the link drop under <c>purchasing.future-allocation.release</c> (reason required, order version unchanged, replay),
/// the plain cancel refused while allocations are active, the drop-and-cancel's exact confirmed set, and the 503 when the host did
/// not wire the participant. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task A_link_drop_releases_the_whole_allocation_with_the_dropped_reason_keeps_the_order_version_and_replays()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var links = new FakeAllocationLinks();
        var allocationId = links.Add(orderId, lineId, version: 2);
        world.AllocationLinks = links;

        var dropped = await world.Service().ReleaseOrderFutureAllocationAsync(orderId, allocationId,
            new ReleasePurchaseFutureAllocationRequest("2", "SENTETIK müşteri vazgeçti"), Metadata("k-drop", correlation: "drop-1"));

        Assert.Equal((orderId, "3", allocationId, false), (dropped.OrderId, dropped.OrderVersion, dropped.AllocationId, dropped.IsReplay));
        var release = Assert.Single(links.Releases);
        Assert.Equal((allocationId, 2L, PurchaseFutureAllocationLinkReasonsV1.LinkDropped, "SENTETIK müşteri vazgeçti"),
            (release.AllocationId, release.ExpectedAllocationVersion, release.ReasonCode, release.ReasonText));
        Assert.Equal(PurchasingPermissionManifestV1.FutureAllocationRelease, world.Authority.LastPermission);
        Assert.Equal(("purchasing.purchase-order.future-allocation-released.v1", "ReleaseFutureAllocation"),
            (world.Store.Audits.Last().EventType, world.Store.Audits.Last().Operation));
        Assert.Equal("3", (await world.Service().GetOrderDetailAsync(orderId)).Order.Version);

        var replay = await world.Service().ReleaseOrderFutureAllocationAsync(orderId, allocationId,
            new ReleasePurchaseFutureAllocationRequest("2", "SENTETIK müşteri vazgeçti"), Metadata("k-drop", correlation: "drop-2"));
        Assert.Equal((true, "drop-1"), (replay.IsReplay, replay.OriginalCorrelationId));
        Assert.Single(links.Releases);
    }

    [Theory]
    [InlineData("", PurchasingProblemCodesV1.FutureAllocationReleaseReasonRequired)]
    [InlineData("  ", PurchasingProblemCodesV1.FutureAllocationReleaseReasonRequired)]
    public async Task A_link_drop_without_a_reason_is_a_422_that_releases_nothing(string reason, string code)
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var links = new FakeAllocationLinks();
        var allocationId = links.Add(orderId, lineId, version: 1);
        world.AllocationLinks = links;

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReleaseOrderFutureAllocationAsync(orderId,
            allocationId, new ReleasePurchaseFutureAllocationRequest("1", reason), Metadata("k-blank")));

        Assert.Equal((422, code, "reasonText"), (refused.Status, refused.Code, refused.Errors[0].Field));
        Assert.Empty(links.Releases);
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task A_stale_allocation_version_is_a_terminal_refusal_recorded_under_the_key_and_replayed()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var links = new FakeAllocationLinks();
        var allocationId = links.Add(orderId, lineId, version: 5);
        world.AllocationLinks = links;

        var stale = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReleaseOrderFutureAllocationAsync(orderId,
            allocationId, new ReleasePurchaseFutureAllocationRequest("4", "SENTETIK"), Metadata("k-stale")));
        Assert.Equal((409, PurchasingProblemCodesV1.FutureAllocationVersionConflict, false), (stale.Status, stale.Code, stale.IsReplay));

        var again = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReleaseOrderFutureAllocationAsync(orderId,
            allocationId, new ReleasePurchaseFutureAllocationRequest("4", "SENTETIK"), Metadata("k-stale", correlation: "retry")));
        Assert.Equal((409, PurchasingProblemCodesV1.FutureAllocationVersionConflict, true), (again.Status, again.Code, again.IsReplay));
        Assert.Single(world.Store.Refusals, item => item.Command == PurchasingCommands.OrderFutureAllocationReleaseV1);
        Assert.Empty(links.Releases);
    }

    [Fact]
    public async Task An_unknown_allocation_is_a_404_and_the_list_shows_only_this_orders_active_links()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var links = new FakeAllocationLinks();
        var allocationId = links.Add(orderId, lineId, version: 1);
        links.Add(Guid.NewGuid(), Guid.NewGuid(), version: 1);
        world.AllocationLinks = links;

        var view = await world.Service().GetOrderFutureAllocationsAsync(orderId);
        Assert.Equal((orderId, "3"), (view.OrderId, view.OrderVersion));
        Assert.Equal(allocationId, Assert.Single(view.Links).AllocationId);
        Assert.Equal(PurchasingPermissionManifestV1.FutureAllocationRelease, world.Authority.LastPermission);

        var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReleaseOrderFutureAllocationAsync(orderId,
            Guid.NewGuid(), new ReleasePurchaseFutureAllocationRequest("1", "SENTETIK"), Metadata("k-missing")));
        Assert.Equal((404, "not_found"), (missing.Status, missing.Code));
        Assert.Empty(links.Releases);
    }

    [Fact]
    public async Task A_plain_cancel_is_refused_while_allocations_are_active_and_the_order_stays_approved()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var links = new FakeAllocationLinks();
        links.Add(orderId, lineId, version: 1);
        world.AllocationLinks = links;

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("3", "SENTETIK"), Metadata("k-plain")));

        Assert.Equal((409, PurchasingProblemCodesV1.LineHasAllocations), (refused.Status, refused.Code));
        Assert.Equal("Approved", (await world.Service().GetOrderDetailAsync(orderId)).Order.Status);
        Assert.Empty(links.Releases);
    }

    [Fact]
    public async Task A_drop_and_cancel_with_a_changed_set_refuses_and_with_the_exact_set_releases_each_and_cancels()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var links = new FakeAllocationLinks();
        var first = links.Add(orderId, lineId, version: 2);
        var second = links.Add(orderId, lineId, version: 7);
        world.AllocationLinks = links;

        var changed = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CancelOrderWithAllocationReleaseAsync(orderId,
            new CancelPurchaseOrderWithAllocationReleaseRequest("3", "SENTETIK", [new(first, "2")]), Metadata("k-changed")));
        Assert.Equal((409, PurchasingProblemCodesV1.FutureAllocationSetChanged), (changed.Status, changed.Code));
        Assert.Empty(links.Releases);

        var cancelled = await world.Service().CancelOrderWithAllocationReleaseAsync(orderId,
            new CancelPurchaseOrderWithAllocationReleaseRequest("3", "SENTETIK tedarik gelmeyecek", [new(first, "2"), new(second, "7")]),
            Metadata("k-drop-cancel"));

        Assert.Equal("Cancelled", cancelled.Detail.Order.Status);
        Assert.Equal(2, links.Releases.Count);
        Assert.All(links.Releases, item => Assert.Equal(PurchaseFutureAllocationLinkReasonsV1.SourceCancelled, item.ReasonCode));
        Assert.Equal(new[] { first, second }.Order(), links.Releases.Select(item => item.AllocationId));
    }

    [Fact]
    public async Task A_duplicate_or_unparsable_confirmed_allocation_is_a_400_before_anything()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var links = new FakeAllocationLinks();
        var allocationId = links.Add(orderId, lineId, version: 1);
        world.AllocationLinks = links;

        var duplicate = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CancelOrderWithAllocationReleaseAsync(orderId,
            new CancelPurchaseOrderWithAllocationReleaseRequest("3", "SENTETIK", [new(allocationId, "1"), new(allocationId, "1")]), Metadata("k-dup")));
        var unparsable = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CancelOrderWithAllocationReleaseAsync(orderId,
            new CancelPurchaseOrderWithAllocationReleaseRequest("3", "SENTETIK", [new(allocationId, "0")]), Metadata("k-zero")));

        Assert.Equal((400, 400), (duplicate.Status, unparsable.Status));
        Assert.Empty(links.Releases);
        Assert.Equal("Approved", (await world.Service().GetOrderDetailAsync(orderId)).Order.Status);
    }

    [Fact]
    public async Task Without_the_participant_the_link_routes_answer_503_and_the_plain_cancel_stays_unguarded()
    {
        var world = new World();
        var (orderId, _) = await ApprovedOrderAsync(world, "10");

        var list = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderFutureAllocationsAsync(orderId));
        var drop = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReleaseOrderFutureAllocationAsync(orderId,
            Guid.NewGuid(), new ReleasePurchaseFutureAllocationRequest("1", "SENTETIK"), Metadata("k-drop")));
        Assert.Equal((503, 503), (list.Status, drop.Status));

        var cancelled = await world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("3", "SENTETIK"), Metadata("k-plain"));
        Assert.Equal("Cancelled", cancelled.Detail.Order.Status);
    }

    [Fact]
    public async Task The_release_name_is_measured_before_the_link_drop_reads_or_writes_anything()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var links = new FakeAllocationLinks();
        var allocationId = links.Add(orderId, lineId, version: 1);
        world.AllocationLinks = links;
        world.Authority.Granted = false;

        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReleaseOrderFutureAllocationAsync(orderId,
            allocationId, new ReleasePurchaseFutureAllocationRequest("1", "SENTETIK"), Metadata("k-denied")));

        Assert.Equal((403, PurchasingPermissionManifestV1.FutureAllocationRelease), (denied.Status, world.Authority.LastPermission));
        Assert.Equal(0, links.ListCalls);
        Assert.Empty(links.Releases);
    }

    /// <summary>An in-memory Inventory owner: active links per purchase line; a release checks the version and removes the link.</summary>
    private sealed class FakeAllocationLinks : IPurchaseFutureAllocationLinkParticipantV1
    {
        private readonly List<PurchaseFutureAllocationLinkV1> _active = [];

        public List<PurchaseFutureAllocationLinkReleaseV1> Releases { get; } = [];

        public int ListCalls { get; private set; }

        public Guid Add(Guid orderId, Guid lineId, long version)
        {
            var id = Guid.NewGuid();
            _active.Add(new PurchaseFutureAllocationLinkV1(id, version, orderId, lineId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "5", "5", DateTimeOffset.UnixEpoch.AddMinutes(_active.Count)));
            return id;
        }

        public Task<IReadOnlyList<PurchaseFutureAllocationLinkV1>> ListActiveAsync(IDbTransaction transaction, Guid purchaseOrderId,
            IReadOnlyList<Guid> purchaseLineIds, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<PurchaseFutureAllocationLinkV1>>(
                [.. _active.Where(link => link.PurchaseOrderId == purchaseOrderId && purchaseLineIds.Contains(link.PurchaseOrderLineId))]);
        }

        public Task<PurchaseFutureAllocationLinkV1> ReleaseAsync(IDbTransaction transaction, PurchaseFutureAllocationLinkReleaseV1 request,
            CancellationToken cancellationToken = default)
        {
            var link = _active.SingleOrDefault(candidate => candidate.AllocationId == request.AllocationId)
                ?? throw new PurchaseFutureAllocationLinkException(409, PurchasingProblemCodesV1.FutureAllocationState, terminal: true);
            if (link.Version != request.ExpectedAllocationVersion)
            {
                throw new PurchaseFutureAllocationLinkException(409, PurchasingProblemCodesV1.FutureAllocationVersionConflict, terminal: true);
            }

            _active.Remove(link);
            Releases.Add(request);
            return Task.FromResult(link with { Version = link.Version + 1, RemainingQuantity = "0" });
        }
    }
}
