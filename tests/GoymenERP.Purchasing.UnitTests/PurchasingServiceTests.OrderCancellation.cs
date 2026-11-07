using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A1-3 (plan API-10; No.161 §2): the cancellation of an approved order's whole unaccepted remainder through the service — the
/// <c>purchasing.orders.cancel</c> name, the terminal refusals recorded under the key and replayed, the reason's 422 (not
/// terminal), the audit event, the detail's cancelled shares and cancellation, the impact preview that writes nothing, and the
/// No.161 §1 link: once the bound approved order is cancelled the request can be rejected. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task An_approved_orders_remainder_is_cancelled_under_the_cancel_name_after_a_partial_acceptance_and_the_same_key_replays()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var stored = (await world.Store.FindOrderAsync(orderId))!.Order;
        stored.RecordAcceptance(3, Guid.NewGuid(), lineId, Quantity.Of(4m, QuantityUnit.Kilogram), DateTimeOffset.UtcNow);

        var cancelled = await world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("4", "SENTETIK kalan gelmeyecek"),
            Metadata("k-cancel", correlation: "cancel-1"));

        Assert.Equal(("Cancelled", "5", false), (cancelled.Detail.Order.Status, cancelled.Detail.Order.Version, cancelled.IsReplay));
        var line = Assert.Single(cancelled.Detail.Lines);
        Assert.Equal(("10", "4", "6", "0"), (line.Committed, line.Accepted, line.Cancelled, line.Remaining));
        Assert.Equal((Actor, "SENTETIK kalan gelmeyecek"), (cancelled.Detail.Cancellation!.CancelledBy, cancelled.Detail.Cancellation.Reason));
        Assert.Equal(PurchasingPermissionManifestV1.OrdersCancel, world.Authority.LastPermission);
        Assert.Equal(("purchasing.purchase-order.remainder-cancelled.v1", "Cancel"),
            (world.Store.Audits.Last().EventType, world.Store.Audits.Last().Operation));
        var envelope = world.Store.Envelopes.Last();
        Assert.Equal((Actor, PurchasingPermissionManifestV1.OrdersCancel, "k-cancel"), (envelope.ActorId, envelope.PermissionCode, envelope.IdempotencyKey));

        var replay = await world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("4", "SENTETIK kalan gelmeyecek"),
            Metadata("k-cancel", correlation: "cancel-2"));
        Assert.Equal((true, "cancel-1", "5"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Order.Version));
        Assert.Single(world.Store.Transitions, item => item.Command == PurchasingCommands.OrderCancelV1);
        var detail = await world.Service().GetOrderDetailAsync(orderId);
        Assert.Equal(("Cancelled", "6", "0"), (detail.Order.Status, detail.Lines[0].Cancelled, detail.Lines[0].Remaining));
        Assert.Equal(Actor, detail.Cancellation!.CancelledBy);
    }

    [Fact]
    public async Task A_draft_order_and_a_cancelled_one_are_terminal_state_refusals_replayed_under_their_keys()
    {
        var world = new World();
        var draft = (await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-create"))).Detail.Order.Id;

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CancelOrderAsync(draft, new CancelPurchaseOrderRequest("2", "SENTETIK"), Metadata("k-cancel-draft")));
        Assert.Equal((409, PurchasingProblemCodesV1.OrderState, "status", PurchasingRuleCodes.OrderNotCancellable, false),
            (refused.Status, refused.Code, refused.Errors[0].Field, refused.Errors[0].Code, refused.IsReplay));
        var again = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CancelOrderAsync(draft, new CancelPurchaseOrderRequest("2", "SENTETIK"), Metadata("k-cancel-draft", correlation: "retry")));
        Assert.Equal((409, PurchasingProblemCodesV1.OrderState, true), (again.Status, again.Code, again.IsReplay));
        Assert.Equal(("Draft", "2"), ((await world.Service().GetOrderDetailAsync(draft)).Order.Status, (await world.Service().GetOrderDetailAsync(draft)).Order.Version));

        var (approved, _) = await ApprovedOrderAsync(world, "5");
        await world.Service().CancelOrderAsync(approved, new CancelPurchaseOrderRequest("3", "SENTETIK"), Metadata("k-cancel-1"));
        var twice = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CancelOrderAsync(approved, new CancelPurchaseOrderRequest("4", "SENTETIK"), Metadata("k-cancel-2")));
        Assert.Equal((409, PurchasingProblemCodesV1.OrderState, PurchasingRuleCodes.OrderNotCancellable), (twice.Status, twice.Code, twice.Errors[0].Code));
        Assert.Equal(2, world.Store.Refusals.Count(item => item.Command == PurchasingCommands.OrderCancelV1));
    }

    [Fact]
    public async Task A_fully_accepted_order_has_nothing_to_cancel_terminally_and_a_stale_version_is_terminal_too()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "5");
        var stale = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("2", "SENTETIK"), Metadata("k-stale")));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));

        (await world.Store.FindOrderAsync(orderId))!.Order.RecordAcceptance(3, Guid.NewGuid(), lineId, Quantity.Of(5m, QuantityUnit.Kilogram),
            DateTimeOffset.UtcNow);
        var full = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("4", "SENTETIK"), Metadata("k-full")));
        Assert.Equal((409, PurchasingProblemCodesV1.OrderNothingToCancel, "lines", PurchasingRuleCodes.OrderNothingToCancel),
            (full.Status, full.Code, full.Errors[0].Field, full.Errors[0].Code));
        Assert.Equal(2, world.Store.Refusals.Count);
        Assert.Equal("Approved", (await world.Service().GetOrderDetailAsync(orderId)).Order.Status);
    }

    [Theory]
    [InlineData(" ", PurchasingProblemCodesV1.OrderCancellationReasonRequired)]
    [InlineData("", PurchasingProblemCodesV1.OrderCancellationReasonRequired)]
    public async Task A_blank_reason_is_a_422_on_the_reason_field_and_is_not_recorded_as_terminal(string reason, string code)
    {
        var world = new World();
        var (orderId, _) = await ApprovedOrderAsync(world, "5");

        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("3", reason), Metadata("k-blank")));

        Assert.Equal((422, code, "reason"), (refused.Status, refused.Code, refused.Errors[0].Field));
        Assert.Empty(world.Store.Refusals);
        var corrected = await world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("3", "SENTETIK"), Metadata("k-corrected"));
        Assert.Equal("Cancelled", corrected.Detail.Order.Status);
    }

    [Fact]
    public async Task The_cancel_name_is_measured_before_anything_and_an_unbound_name_is_a_setup_problem()
    {
        var world = new World();
        var (orderId, _) = await ApprovedOrderAsync(world, "5");
        world.Authority.Granted = false;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("3", "SENTETIK"), Metadata("k-denied")));
        Assert.Equal((403, "forbidden", PurchasingPermissionManifestV1.OrdersCancel), (denied.Status, denied.Code, world.Authority.LastPermission));
        var deniedPreview = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderCancelImpactAsync(orderId));
        Assert.Equal((403, PurchasingPermissionManifestV1.OrdersCancel), (deniedPreview.Status, world.Authority.LastPermission));

        world.Authority.Granted = true;
        world.Bindings = world.Bindings with { OrderCancelPermission = null };
        var unbound = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("3", "SENTETIK"), Metadata("k-unbound")));
        Assert.Equal((503, PurchasingProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));

        var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            new World().Service().CancelOrderAsync(Guid.NewGuid(), new CancelPurchaseOrderRequest("3", "SENTETIK"), Metadata("k-missing")));
        Assert.Equal(404, missing.Status);
        var missingPreview = await Assert.ThrowsAsync<PurchasingServiceException>(() => new World().Service().GetOrderCancelImpactAsync(Guid.NewGuid()));
        Assert.Equal(404, missingPreview.Status);
        Assert.Empty(world.Store.Refusals);
        Assert.Equal("Approved", (await world.Service().GetOrderDetailAsync(orderId)).Order.Status);
    }

    [Fact]
    public async Task The_impact_preview_names_the_remainder_to_cancel_or_the_blocker_and_writes_nothing()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        (await world.Store.FindOrderAsync(orderId))!.Order.RecordAcceptance(3, Guid.NewGuid(), lineId, Quantity.Of(4m, QuantityUnit.Kilogram),
            DateTimeOffset.UtcNow);
        var envelopes = world.Store.Envelopes.Count;

        var impact = await world.Service().GetOrderCancelImpactAsync(orderId);

        Assert.Equal((orderId, "Approved", "4", true, (string?)null, true), (impact.OrderId, impact.Status, impact.Version, impact.Cancellable, impact.Blocker,
            impact.HasAcceptance));
        var line = Assert.Single(impact.Lines);
        Assert.Equal((lineId, "10", "4", "6"), (line.LineId, line.Committed, line.Accepted, line.ToCancel));
        Assert.Equal(PurchasingPermissionManifestV1.OrdersCancel, world.Authority.LastPermission);
        Assert.Equal(envelopes, world.Store.Envelopes.Count);

        var draft = (await world.Service().CreateOrderAsync(OrderOf(null, null, "5"), Metadata("k-draft"))).Detail.Order.Id;
        var blocked = await world.Service().GetOrderCancelImpactAsync(draft);
        Assert.Equal((false, PurchasingRuleCodes.OrderNotCancellable, "0"), (blocked.Cancellable, blocked.Blocker, blocked.Lines[0].ToCancel));

        await world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("4", "SENTETIK"), Metadata("k-cancel"));
        var after = await world.Service().GetOrderCancelImpactAsync(orderId);
        Assert.Equal((false, PurchasingRuleCodes.OrderNotCancellable, "Cancelled"), (after.Cancellable, after.Blocker, after.Status));
    }

    [Fact]
    public async Task Once_the_bound_approved_order_is_cancelled_the_request_can_be_rejected_No161()
    {
        var world = new World();
        var created = await world.Service().CreateRequestAsync(RequestOf("100"), Metadata("k-req"));
        var requestId = created.Detail.Request.Id;
        await world.Service().SubmitRequestAsync(requestId, new SubmitPurchaseRequestRequest("2"), Metadata("k-submit"));
        await world.ApproveRequestAsApprover(requestId, "3");
        var orderId = (await world.Service().CreateOrderAsync(OrderOf(requestId, created.Detail.Lines[0].LineId, "40"), Metadata("k-order"))).Detail.Order.Id;
        await world.Service().ApproveOrderAsync(orderId, new ApprovePurchaseOrderRequest("2"), Metadata("k-order-approve"));

        var blocked = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().RejectRequestAsync(requestId, new RejectPurchaseRequestRequest("4", "SENTETIK"), Metadata("k-reject-1"))));
        Assert.Equal(PurchasingProblemCodesV1.RequestHasApprovedOrder, blocked.Code);

        var cancelled = await world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("3", "SENTETIK"), Metadata("k-cancel"));
        Assert.Equal((requestId, created.Detail.Lines[0].LineId), (cancelled.Detail.Order.SourceRequestId, cancelled.Detail.Lines[0].SourceRequestLineId));

        var rejected = await world.AsApprover(() =>
            world.Service().RejectRequestAsync(requestId, new RejectPurchaseRequestRequest("4", "SENTETIK"), Metadata("k-reject-2")));
        Assert.Equal("Rejected", rejected.Detail.Request.Status);
    }

    [Fact]
    public void The_cancellation_wire_shape_is_strict_and_the_reason_is_in_the_content_hash()
    {
        Assert.Equal(("3", "neden"), (PurchasingJson.ReadCancelOrder("{\"expectedVersion\":\"3\",\"reason\":\"neden\"}").ExpectedVersion,
            PurchasingJson.ReadCancelOrder("{\"expectedVersion\":\"3\",\"reason\":\"neden\"}").Reason));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadCancelOrder("{\"expectedVersion\":\"3\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadCancelOrder("{\"expectedVersion\":\"3\",\"reason\":null}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadCancelOrder("{\"expectedVersion\":\"0\",\"reason\":\"x\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadCancelOrder("{\"expectedVersion\":3,\"reason\":\"x\"}"));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadCancelOrder("{\"expectedVersion\":\"3\",\"reason\":\"x\",\"lineId\":\"y\"}"));
        Assert.NotEqual(PurchasingRequestHash.Compute(PurchasingCommands.OrderCancelV1, Guid.Empty, new CancelPurchaseOrderRequest("3", "a")),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderCancelV1, Guid.Empty, new CancelPurchaseOrderRequest("3", "b")));
        Assert.True(PurchasingCommands.IsKnown(PurchasingCommands.OrderCancelV1));
        Assert.True(PurchasingCommands.IsOrderMutation(PurchasingCommands.OrderCancelV1));
        Assert.False(PurchasingCommands.IsRequestMutation(PurchasingCommands.OrderCancelV1));
    }

    private static async Task<(Guid Order, Guid Line)> ApprovedOrderAsync(World world, string quantity)
    {
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, quantity), Metadata($"k-create-{Guid.NewGuid():N}"));
        await world.Service().ApproveOrderAsync(created.Detail.Order.Id, new ApprovePurchaseOrderRequest("2"), Metadata($"k-approve-{Guid.NewGuid():N}"));
        return (created.Detail.Order.Id, created.Detail.Lines[0].LineId);
    }
}
