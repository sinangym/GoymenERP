using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-027 (ALS-D94, ALS-D81) on real SQL Server: a line's delivery-schedule history is read from the append-only slice rows as stored —
/// one revision per order version that recorded it, oldest first, with its recorder, time and slices; a termin change on the approved
/// order stays visible beside the first schedule; another line or company B reads none; a revision whose rows disagree on the
/// recorder is refused, never merged. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_line_schedule_history_reads_every_revision_oldest_first_and_company_B_reads_none()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, approvedVersion) = await ApprovedAcceptanceOrderAsync(run, 100m);
        var first = new[] { KgSlice(100m, ScheduleD1) };
        var retimed = new[] { KgSlice(60m, ScheduleD1), KgSlice(40m, ScheduleD2) };

        await using (StoreScope a = run.Store(CompanyA))
        {
            Assert.Empty(await a.Store.ListLineScheduleRevisionsAsync(orderId, lineId));
            await a.Store.MutateOrderAsync(run.Envelope("k-history-1"), Hash("k-history-1"), PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(approvedVersion, lineId, first, run.Actor, now), LineScheduleSet);
            await a.Store.MutateOrderAsync(run.Envelope("k-history-2"), Hash("k-history-2"), PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(approvedVersion + 1, lineId, retimed, run.Actor, now), LineScheduleSet);
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            var revisions = await a.Store.ListLineScheduleRevisionsAsync(orderId, lineId);
            Assert.Equal([(approvedVersion + 1, run.Actor), (approvedVersion + 2, run.Actor)],
                revisions.Select(revision => (revision.OrderVersionAfter, revision.RecordedBy)).ToArray());
            Assert.True(revisions[0].RecordedAtUtc <= revisions[1].RecordedAtUtc);
            Assert.Equal([(first[0].SliceId, 1, 100m, ScheduleD1)],
                revisions[0].Slices.Select(slice => (slice.SliceId, slice.SliceNo, slice.Quantity, slice.DeliveryDate)).ToArray());
            Assert.Equal([(retimed[0].SliceId, 1, 60m, ScheduleD1), (retimed[1].SliceId, 2, 40m, ScheduleD2)],
                revisions[1].Slices.Select(slice => (slice.SliceId, slice.SliceNo, slice.Quantity, slice.DeliveryDate)).ToArray());
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal(rebuilt.Lines[0].DeliverySchedule.Select(slice => (slice.SliceId, slice.Quantity.Value)).ToArray(),
                revisions[^1].Slices.Select(slice => (slice.SliceId, slice.Quantity)).ToArray());
            Assert.Empty(await a.Store.ListLineScheduleRevisionsAsync(orderId, Guid.NewGuid()));
        }

        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Empty(await b.Store.ListLineScheduleRevisionsAsync(orderId, lineId));
        }

        // A revision whose rows name two recorders does not read as one recording: refused, never merged.
        await run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrderLineDeliverySlices SET RecordedBy = N'SENTETIK-other' WHERE OrderId = @id AND OrderVersionAfter = @v AND SliceNo = 2",
            ("@id", orderId), ("@v", approvedVersion + 2));
        await using (StoreScope a = run.Store(CompanyA))
        {
            var inconsistent = await Assert.ThrowsAsync<PurchasingStoreException>(() => a.Store.ListLineScheduleRevisionsAsync(orderId, lineId));
            Assert.Equal(PurchasingStoreCodes.ScheduleHistoryInconsistent, inconsistent.Code);
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLineDeliverySlices WHERE OrderId = @id", ("@id", orderId)));
    }
}
