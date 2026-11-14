using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// F-027 (ALS-D94, ALS-D81): a purchase order line's delivery schedule history through the service — the read name, every revision
/// oldest first with its recorder, time and slices, the termin change on an approved order visible beside the Draft one, the 404s,
/// the read fences and the fail-closed 503 when the history read beside the order disagrees with it. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task A_lines_schedule_history_lists_the_draft_and_the_approved_termin_change_oldest_first_under_the_read_name()
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10"), Metadata("k-create"));
        var orderId = created.Detail.Order.Id;
        var lineId = created.Detail.Lines[0].LineId;

        var empty = await world.Service().GetOrderLineScheduleHistoryAsync(orderId, lineId);
        Assert.Equal((orderId, lineId, "2", 0), (empty.OrderId, empty.LineId, empty.OrderVersion, empty.Revisions.Count));
        Assert.Equal(ReadPermission, world.Authority.LastPermission);

        var draft = Schedule("2", ("10", ScheduleD1));
        await world.Service().SetOrderLineScheduleAsync(orderId, lineId, draft, Metadata("k-draft"));
        await world.AsApprover(() => world.Service().ApproveOrderAsync(orderId, new ApprovePurchaseOrderRequest("3"), Metadata("k-approve")));
        var retime = Schedule("4", ("4", ScheduleD1), ("6", ScheduleD2));
        var retimed = await world.Service().SetOrderLineScheduleAsync(orderId, lineId, retime, Metadata("k-retime"));
        Assert.Equal(("Approved", "5"), (retimed.Detail.Order.Status, retimed.Detail.Order.Version));

        var history = await world.Service().GetOrderLineScheduleHistoryAsync(orderId, lineId);

        Assert.Equal(ReadPermission, world.Authority.LastPermission);
        Assert.Equal("5", history.OrderVersion);
        Assert.Equal([("3", Actor), ("5", Actor)], history.Revisions.Select(revision => (revision.OrderVersion, revision.RecordedBy)).ToArray());
        Assert.All(history.Revisions, revision => Assert.NotEqual(default, revision.RecordedAtUtc));
        Assert.Equal([(draft.Slices[0].SliceId, 1, "10", ScheduleD1)],
            history.Revisions[0].Slices.Select(slice => (slice.SliceId, slice.SliceNo, slice.Quantity, slice.DeliveryDate)).ToArray());
        Assert.Equal([(retime.Slices[0].SliceId, 1, "4", ScheduleD1), (retime.Slices[1].SliceId, 2, "6", ScheduleD2)],
            history.Revisions[1].Slices.Select(slice => (slice.SliceId, slice.SliceNo, slice.Quantity, slice.DeliveryDate)).ToArray());
        var detail = await world.Service().GetOrderDetailAsync(orderId);
        Assert.Equal(detail.Lines[0].DeliverySchedule, history.Revisions[^1].Slices);
    }

    [Fact]
    public async Task An_unknown_order_or_line_is_a_code_less_404_and_a_refused_or_unbound_read_name_reads_no_history()
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10"), Metadata("k-create"));
        var orderId = created.Detail.Order.Id;
        var lineId = created.Detail.Lines[0].LineId;

        foreach (var (order, line) in new[] { (Guid.Empty, lineId), (orderId, Guid.Empty), (Guid.NewGuid(), lineId), (orderId, Guid.NewGuid()) })
        {
            var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderLineScheduleHistoryAsync(order, line));
            Assert.Equal((404, "not_found"), (missing.Status, missing.Code));
        }

        Assert.Equal(0, world.Store.ScheduleHistoryReads);

        world.Authority.Denied.Add(ReadPermission);
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderLineScheduleHistoryAsync(orderId, lineId));
        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        world.Authority.Denied.Remove(ReadPermission);

        world.Bindings = world.Bindings with { ReadPermission = null };
        var unbound = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderLineScheduleHistoryAsync(orderId, lineId));
        Assert.Equal((503, PurchasingProblemCodesV1.PermissionUnregistered), (unbound.Status, unbound.Code));
        Assert.Equal(0, world.Store.ScheduleHistoryReads);
    }

    [Fact]
    public async Task A_revision_past_the_order_read_is_left_out_and_a_history_that_disagrees_with_the_current_schedule_is_a_503()
    {
        var world = new World();
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10"), Metadata("k-create"));
        var orderId = created.Detail.Order.Id;
        var lineId = created.Detail.Lines[0].LineId;
        await world.Service().SetOrderLineScheduleAsync(orderId, lineId, Schedule("2", ("10", ScheduleD1)), Metadata("k-schedule"));

        // A revision recorded after the order was read (a later version) is not answered beside the older order.
        world.Store.ScheduleRevisions.Add((orderId, lineId, new StoredScheduleRevision(4, Approver, DateTimeOffset.UnixEpoch,
            [new StoredScheduleSlice(Guid.NewGuid(), 1, 10m, ScheduleD2)])));
        var history = await world.Service().GetOrderLineScheduleHistoryAsync(orderId, lineId);
        Assert.Equal(["3"], history.Revisions.Select(revision => revision.OrderVersion).ToArray());

        // The last visible revision must be the line's current schedule.
        var current = world.Store.ScheduleRevisions[0];
        world.Store.ScheduleRevisions[0] = current with
        {
            Revision = current.Revision with { Slices = [current.Revision.Slices[0] with { DeliveryDate = ScheduleD2 }] },
        };
        var disagreeing = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderLineScheduleHistoryAsync(orderId, lineId));
        Assert.Equal((503, "service_unavailable"), (disagreeing.Status, disagreeing.Code));

        // No revision while the line carries a schedule is the same fault.
        world.Store.ScheduleRevisions.Clear();
        var vanished = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderLineScheduleHistoryAsync(orderId, lineId));
        Assert.Equal(503, vanished.Status);

        // The SQL store's inconsistent-revision refusal is a 503 too, never a guess.
        world.Store.ScheduleHistoryFault = new PurchasingStoreException(PurchasingStoreCodes.ScheduleHistoryInconsistent, "synthetic");
        var inconsistent = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().GetOrderLineScheduleHistoryAsync(orderId, lineId));
        Assert.Equal((503, "service_unavailable"), (inconsistent.Status, inconsistent.Code));
    }
}
