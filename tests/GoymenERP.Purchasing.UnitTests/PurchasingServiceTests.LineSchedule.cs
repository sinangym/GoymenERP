using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A3 API-30 (ALS-D33, ALS-D21, master §7.3): a purchase order line's delivery schedule through the service — the create name, the
/// detail's deliverySchedule, the replay under the key (the line id is in the replay identity), the terminal refusals (cancelled
/// order, stale version) replayed, the domain rules' 422 (not terminal), the unknown line's 404, the quantity texts' 400 and the
/// strict wire shape. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static readonly DateOnly ScheduleD1 = new(2026, 12, 1);
    private static readonly DateOnly ScheduleD2 = new(2027, 1, 15);

    private static SetPurchaseOrderLineScheduleRequest Schedule(string version, params (string Quantity, DateOnly Date)[] slices) =>
        new(version, [.. slices.Select(slice => new PurchaseOrderDeliverySliceRequest(Guid.NewGuid(), slice.Quantity, slice.Date))]);

    [Fact]
    public async Task A_draft_lines_schedule_is_recorded_under_the_create_binding_shown_on_the_detail_and_the_same_key_replays()
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10"), Metadata("k-create"));
        var orderId = created.Detail.Order.Id;
        var lineId = created.Detail.Lines[0].LineId;
        var request = Schedule("2", ("6.5", ScheduleD2), ("3.5", ScheduleD1));

        var set = await world.Service().SetOrderLineScheduleAsync(orderId, lineId, request, Metadata("k-schedule", correlation: "schedule-1"));

        Assert.Equal(("Draft", "3", false), (set.Detail.Order.Status, set.Detail.Order.Version, set.IsReplay));
        Assert.Equal([(request.Slices[0].SliceId, 1, "6.5", ScheduleD2), (request.Slices[1].SliceId, 2, "3.5", ScheduleD1)],
            set.Detail.Lines[0].DeliverySchedule.Select(slice => (slice.SliceId, slice.SliceNo, slice.Quantity, slice.DeliveryDate)).ToArray());
        Assert.Equal(CreatePermission, world.Authority.LastPermission);
        Assert.Equal(("purchasing.purchase-order.line-schedule-set.v1", "Schedule"),
            (world.Store.Audits.Last().EventType, world.Store.Audits.Last().Operation));
        Assert.Equal((Actor, CreatePermission, "k-schedule"),
            (world.Store.Envelopes.Last().ActorId, world.Store.Envelopes.Last().PermissionCode, world.Store.Envelopes.Last().IdempotencyKey));

        var replay = await world.Service().SetOrderLineScheduleAsync(orderId, lineId, request, Metadata("k-schedule", correlation: "schedule-2"));
        Assert.Equal((true, "schedule-1", "3"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Order.Version));
        Assert.Single(world.Store.Transitions, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        var detail = await world.Service().GetOrderDetailAsync(orderId);
        Assert.Equal(["6.5", "3.5"], detail.Lines[0].DeliverySchedule.Select(slice => slice.Quantity).ToArray());
    }

    [Fact]
    public async Task An_approved_lines_schedule_is_replaced_whole_and_a_cancelled_order_or_stale_version_is_terminal_and_replayed()
    {
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        await world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("3", ("10", ScheduleD1)), Metadata("k-s1"));
        var replaced = await world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("4", ("4", ScheduleD1), ("6", ScheduleD2)),
            Metadata("k-s2"));
        Assert.Equal(("Approved", "5", 2), (replaced.Detail.Order.Status, replaced.Detail.Order.Version, replaced.Detail.Lines[0].DeliverySchedule.Count));

        var stale = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("4", ("10", ScheduleD1)), Metadata("k-stale")));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, "expectedVersion", false),
            (stale.Status, stale.Code, stale.Errors[0].Field, stale.IsReplay));

        await world.Service().CancelOrderAsync(orderId, new CancelPurchaseOrderRequest("5", "SENTETIK"), Metadata("k-cancel"));
        var cancelledRequest = Schedule("6", ("10", ScheduleD1));
        var cancelled = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, cancelledRequest, Metadata("k-on-cancelled")));
        Assert.Equal((409, PurchasingProblemCodesV1.OrderState, "status", PurchasingRuleCodes.OrderCancelled),
            (cancelled.Status, cancelled.Code, cancelled.Errors[0].Field, cancelled.Errors[0].Code));
        var again = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, cancelledRequest, Metadata("k-on-cancelled", correlation: "retry")));
        Assert.Equal((409, PurchasingProblemCodesV1.OrderState, true), (again.Status, again.Code, again.IsReplay));
        Assert.Equal(2, world.Store.Refusals.Count(item => item.Command == PurchasingCommands.OrderLineScheduleV1));
        Assert.Equal(2, world.Store.Transitions.Count(item => item.Command == PurchasingCommands.OrderLineScheduleV1));
    }

    [Fact]
    public async Task The_domain_rules_are_a_422_on_slices_an_unknown_line_a_404_and_a_bad_quantity_text_a_400_none_terminal()
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10"), Metadata("k-create"));
        var orderId = created.Detail.Order.Id;
        var lineId = created.Detail.Lines[0].LineId;

        var mismatch = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("2", ("9.999", ScheduleD1)), Metadata("k-mismatch")));
        Assert.Equal((422, "validation_failed", "slices", PurchasingRuleCodes.ScheduleTotalMismatch),
            (mismatch.Status, mismatch.Code, mismatch.Errors[0].Field, mismatch.Errors[0].Code));
        var sliceId = Guid.NewGuid();
        var duplicate = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().SetOrderLineScheduleAsync(orderId, lineId,
            new SetPurchaseOrderLineScheduleRequest("2", [new(sliceId, "5", ScheduleD1), new(sliceId, "5", ScheduleD2)]), Metadata("k-duplicate")));
        Assert.Equal((422, PurchasingRuleCodes.ScheduleDuplicateSliceId), (duplicate.Status, duplicate.Errors[0].Code));

        var unknown = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, Guid.NewGuid(), Schedule("2", ("10", ScheduleD1)), Metadata("k-unknown-line")));
        Assert.Equal((404, "not_found"), (unknown.Status, unknown.Code));
        var noOrder = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(Guid.NewGuid(), lineId, Schedule("2", ("10", ScheduleD1)), Metadata("k-unknown-order")));
        Assert.Equal(404, noOrder.Status);
        var emptyLine = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, Guid.Empty, Schedule("2", ("10", ScheduleD1)), Metadata("k-empty-line")));
        Assert.Equal(404, emptyLine.Status);

        var text = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("2", ("1e1", ScheduleD1)), Metadata("k-text")));
        Assert.Equal((400, "validation_failed", "slices[0].quantity"), (text.Status, text.Code, text.Errors[0].Field));
        var scale = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("2", ("5", ScheduleD1), ("4.9999", ScheduleD2)), Metadata("k-scale")));
        Assert.Equal((422, PurchasingProblemCodesV1.UnsupportedUnit, "slices[1].quantity"), (scale.Status, scale.Code, scale.Errors[0].Field));

        Assert.DoesNotContain(world.Store.Refusals, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        // Not terminal: the same key succeeds once the body is right (a different body under a fresh key here).
        var fixedUp = await world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("2", ("10", ScheduleD1)), Metadata("k-fixed"));
        Assert.Equal("3", fixedUp.Detail.Order.Version);
    }

    [Fact]
    public void The_schedule_wire_shape_is_strict_and_the_line_and_slices_are_in_the_content_hash()
    {
        var id = Guid.NewGuid();
        var read = PurchasingJson.ReadSetLineSchedule(
            $"{{\"expectedVersion\":\"2\",\"slices\":[{{\"sliceId\":\"{id}\",\"quantity\":\"10\",\"deliveryDate\":\"2026-12-01\"}}]}}");
        Assert.Equal(("2", id, "10", ScheduleD1), (read.ExpectedVersion, read.Slices[0].SliceId, read.Slices[0].Quantity, read.Slices[0].DeliveryDate));

        string Body(string slices) => $"{{\"expectedVersion\":\"2\",\"slices\":{slices}}}";
        string SliceJson(string sliceId, string quantity = "\"1\"", string date = "\"2026-12-01\"") =>
            $"{{\"sliceId\":\"{sliceId}\",\"quantity\":{quantity},\"deliveryDate\":{date}}}";
        foreach (var bad in new[]
                 {
                     Body("[]"),
                     Body("null"),
                     Body("[null]"),
                     Body($"[{SliceJson(Guid.Empty.ToString())}]"),
                     Body($"[{SliceJson(id.ToString())},{SliceJson(id.ToString())}]"),
                     Body($"[{SliceJson(id.ToString(), "\"0\"")}]"),
                     Body($"[{SliceJson(id.ToString(), "1")}]"),
                     Body($"[{SliceJson(id.ToString(), date: "\"0001-01-01\"")}]"),
                     Body($"[{SliceJson(id.ToString(), date: "null")}]"),
                     $"{{\"expectedVersion\":\"0\",\"slices\":[{SliceJson(id.ToString())}]}}",
                     $"{{\"slices\":[{SliceJson(id.ToString())}]}}",
                     $"{{\"expectedVersion\":\"2\",\"slices\":[{SliceJson(id.ToString())}],\"lineId\":\"x\"}}",
                 })
        {
            Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadSetLineSchedule(bad));
        }

        var tooMany = string.Join(",", Enumerable.Range(0, 501).Select(_ => SliceJson(Guid.NewGuid().ToString())));
        Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadSetLineSchedule(Body($"[{tooMany}]")));

        var request = new SetPurchaseOrderLineScheduleRequest("2", [new(id, "10", ScheduleD1)]);
        var lineA = Guid.NewGuid();
        Assert.NotEqual(PurchasingRequestHash.Compute(PurchasingCommands.OrderLineScheduleV1, id, new { lineId = lineA, request }),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderLineScheduleV1, id, new { lineId = Guid.NewGuid(), request }));
        Assert.True(PurchasingCommands.IsKnown(PurchasingCommands.OrderLineScheduleV1));
        Assert.True(PurchasingCommands.IsOrderMutation(PurchasingCommands.OrderLineScheduleV1));
        Assert.False(PurchasingCommands.IsRequestMutation(PurchasingCommands.OrderLineScheduleV1));
    }
}
