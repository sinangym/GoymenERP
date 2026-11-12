using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Domain.Orders;
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
    public async Task After_a_partial_acceptance_re_dating_the_received_part_is_a_non_terminal_422_and_the_remaining_part_moves()
    {
        // ALS-D81 (Sinan #66 6816723313 madde 3): the received part's termin is locked; only the remaining quantity's changes.
        var world = new World();
        var (orderId, lineId) = await ApprovedOrderAsync(world, "10");
        var first = Schedule("3", ("10", ScheduleD1));
        await world.Service().SetOrderLineScheduleAsync(orderId, lineId, first, Metadata("k-s1"));
        (await world.Store.FindOrderAsync(orderId))!.Order.RecordAcceptance(4, Guid.NewGuid(), lineId, Quantity.Of(4m, QuantityUnit.Kilogram),
            DateTimeOffset.UtcNow);

        var locked = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("5", ("10", ScheduleD2)), Metadata("k-locked")));
        Assert.Equal((422, "validation_failed", "slices", PurchasingRuleCodes.ScheduleReceivedPortionLocked, false),
            (locked.Status, locked.Code, locked.Errors[0].Field, locked.Errors[0].Code, locked.IsReplay));
        Assert.DoesNotContain(world.Store.Refusals, item => item.Command == PurchasingCommands.OrderLineScheduleV1);

        var kept = first.Slices[0] with { Quantity = "4" };
        var moved = await world.Service().SetOrderLineScheduleAsync(orderId, lineId,
            new SetPurchaseOrderLineScheduleRequest("5", [kept, new(Guid.NewGuid(), "6", ScheduleD2)]), Metadata("k-moved"));
        Assert.Equal([(kept.SliceId, "4", ScheduleD1), (moved.Detail.Lines[0].DeliverySchedule[1].SliceId, "6", ScheduleD2)],
            moved.Detail.Lines[0].DeliverySchedule.Select(slice => (slice.SliceId, slice.Quantity, slice.DeliveryDate)).ToArray());
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
    public async Task A_fraction_of_a_piece_in_a_slice_is_the_same_non_terminal_422_scale_refusal_as_an_order_line_quantity()
    {
        // The order line's precedent (VerifyUnitAsync): a quantity finer than the unit's scale is 422 unsupported_unit with
        // purchasing_quantity_scale_exceeded, never a 400; a slice names its own quantity.
        var world = new World();
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Party, DateTimeOffset.UnixEpoch, Actor);
        var line = order.AddLine(1, Guid.NewGuid(), Product, Guid.Parse("66666666-6666-4666-8666-666666666666"),
            Quantity.Of(10m, QuantityUnit.Piece), DateTimeOffset.UnixEpoch);
        world.Store.PlantOrder(new StoredPurchaseOrder(order, Account, Day, new Dictionary<Guid, string> { [line.Id] = "7" }));

        var fraction = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().SetOrderLineScheduleAsync(order.Id, line.Id,
            Schedule("2", ("4", ScheduleD1), ("5.5", ScheduleD2), ("0.5", ScheduleD2)), Metadata("k-piece-fraction")));

        Assert.Equal((422, PurchasingProblemCodesV1.UnsupportedUnit, "slices[1].quantity", PurchaseUnitCodes.QuantityScaleExceeded),
            (fraction.Status, fraction.Code, fraction.Errors.Single().Field, fraction.Errors.Single().Code));
        Assert.Equal((2, 0), (order.Version, line.DeliverySchedule.Count));
        Assert.DoesNotContain(world.Store.Refusals, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        var whole = await world.Service().SetOrderLineScheduleAsync(order.Id, line.Id, Schedule("2", ("4", ScheduleD1), ("6", ScheduleD2)),
            Metadata("k-piece-fraction"));
        Assert.Equal(("3", "6"), (whole.Detail.Order.Version, whole.Detail.Lines[0].DeliverySchedule[1].Quantity));
    }

    [Fact]
    public async Task The_create_name_is_measured_first_an_anonymous_caller_is_401_a_refused_one_403_and_an_unbound_name_503_nothing_written()
    {
        // k3-command-skeleton: the schedule command's own 401 / 403 / 503 fences, each before the replay read and the store.
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10"), Metadata("k-create"));
        var orderId = created.Detail.Order.Id;
        var lineId = created.Detail.Lines[0].LineId;
        var replaysBefore = world.Store.Replays;

        world.User.CurrentActor = " ";
        var anonymous = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("2", ("10", ScheduleD1)), Metadata("k-anonymous")));
        Assert.Equal((401, "unauthenticated"), (anonymous.Status, anonymous.Code));
        world.User.CurrentActor = Actor;

        world.Authority.Granted = false;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("2", ("10", ScheduleD1)), Metadata("k-denied")));
        Assert.Equal((403, "forbidden", CreatePermission), (denied.Status, denied.Code, world.Authority.LastPermission));
        world.Authority.Granted = true;

        world.Bindings = world.Bindings with { CreatePermission = null };
        var unbound = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("2", ("10", ScheduleD1)), Metadata("k-unbound")));
        Assert.Equal((503, PurchasingProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));

        Assert.Equal(replaysBefore, world.Store.Replays);
        Assert.DoesNotContain(world.Store.Refusals, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        var order = (await world.Store.FindOrderAsync(orderId))!.Order;
        Assert.Equal((2, 0), (order.Version, order.Lines[0].DeliverySchedule.Count));
    }

    [Fact]
    public async Task The_same_key_with_another_body_or_another_line_is_the_409_same_key_payload_mismatch_and_writes_nothing()
    {
        var world = new World();
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Party, DateTimeOffset.UnixEpoch, Actor);
        var first = order.AddLine(1, Guid.NewGuid(), Product, Guid.Parse("66666666-6666-4666-8666-666666666666"),
            Quantity.Of(10m, QuantityUnit.Kilogram), DateTimeOffset.UnixEpoch);
        var second = order.AddLine(2, Guid.NewGuid(), Product, Guid.Parse("66666666-6666-4666-8666-666666666666"),
            Quantity.Of(10m, QuantityUnit.Kilogram), DateTimeOffset.UnixEpoch);
        world.Store.PlantOrder(new StoredPurchaseOrder(order, Account, Day,
            new Dictionary<Guid, string> { [first.Id] = "7", [second.Id] = "7" }));
        var request = Schedule(order.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), ("10", ScheduleD1));
        var set = await world.Service().SetOrderLineScheduleAsync(order.Id, first.Id, request, Metadata("k-mismatch"));
        var version = set.Detail.Order.Version;

        var otherBody = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().SetOrderLineScheduleAsync(order.Id, first.Id,
            request with { Slices = [request.Slices[0] with { DeliveryDate = ScheduleD2 }] }, Metadata("k-mismatch")));
        Assert.Equal((409, PurchasingProblemCodesV1.SameKeyPayloadMismatch), (otherBody.Status, otherBody.Code));
        var otherLine = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(order.Id, second.Id, request, Metadata("k-mismatch")));
        Assert.Equal((409, PurchasingProblemCodesV1.SameKeyPayloadMismatch), (otherLine.Status, otherLine.Code));

        Assert.Single(world.Store.Transitions, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        Assert.DoesNotContain(world.Store.Refusals, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        var detail = await world.Service().GetOrderDetailAsync(order.Id);
        Assert.Equal((version, ScheduleD1, 0), (detail.Order.Version, detail.Lines[0].DeliverySchedule.Single().DeliveryDate,
            detail.Lines.Single(line => line.LineId == second.Id).DeliverySchedule.Count));
    }

    [Fact]
    public async Task A_grant_lost_between_the_first_fence_and_the_write_is_403_at_the_last_fence_before_the_store()
    {
        // RequireStillAsync: the grant is measured again after the replay read; losing it there refuses before any mutation.
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10"), Metadata("k-create"));
        var orderId = created.Detail.Order.Id;
        var lineId = created.Detail.Lines[0].LineId;
        var mutated = false;
        world.Store.BeforeMutation = () => mutated = true;
        world.Store.OnReplayRead = () => world.Authority.Granted = false;

        var lost = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("2", ("10", ScheduleD1)), Metadata("k-lost")));

        Assert.Equal((403, "forbidden", false, null), (lost.Status, lost.Code, lost.IsReplay, lost.OriginalCorrelationId));
        Assert.False(mutated);
        Assert.DoesNotContain(world.Store.Refusals, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        Assert.Equal(2, (await world.Store.FindOrderAsync(orderId))!.Order.Version);
    }

    [Fact]
    public async Task On_a_piece_line_an_unknown_line_or_a_stale_version_answers_before_the_fraction_refusal_404_and_terminal_409()
    {
        // K3 review P3: the unit-scale check reads the line's own unit only after the domain's version, cancelled and line refusals
        // (a text finer than any unit's scale, e.g. four decimals, stays the body's own 422 before the store).
        var world = new World();
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Party, DateTimeOffset.UnixEpoch, Actor);
        var line = order.AddLine(1, Guid.NewGuid(), Product, Guid.Parse("66666666-6666-4666-8666-666666666666"),
            Quantity.Of(10m, QuantityUnit.Piece), DateTimeOffset.UnixEpoch);
        world.Store.PlantOrder(new StoredPurchaseOrder(order, Account, Day, new Dictionary<Guid, string> { [line.Id] = "7" }));

        var unknown = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(order.Id, Guid.NewGuid(), Schedule("2", ("9.5", ScheduleD1)), Metadata("k-unknown-fraction")));
        Assert.Equal((404, "not_found"), (unknown.Status, unknown.Code));
        var stale = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(order.Id, line.Id, Schedule("1", ("9.5", ScheduleD1)), Metadata("k-stale-fraction")));
        Assert.Equal((409, PurchasingProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));
        var fraction = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().SetOrderLineScheduleAsync(order.Id, line.Id, Schedule("2", ("9.5", ScheduleD1)), Metadata("k-current-fraction")));
        Assert.Equal((422, PurchasingProblemCodesV1.UnsupportedUnit, "slices[0].quantity"), (fraction.Status, fraction.Code, fraction.Errors[0].Field));
        Assert.Single(world.Store.Refusals, item => item.Command == PurchasingCommands.OrderLineScheduleV1);
        Assert.Equal((2, 0), (order.Version, line.DeliverySchedule.Count));
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
