using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A3 API-30 (ALS-D33, ALS-D21, master §7.3) on real SQL Server: a general line's delivery schedule (termin dilimleri) is stored as an
/// append-only revision at the order version it produced, in ONE save with the header version, the attempt and the audit; the same
/// key replays; a later revision after an acceptance keeps the earlier one as history and the order is rebuilt revision by revision
/// between the acceptance shares; two concurrent schedules of one line yield one revision; a domain refusal writes nothing; the checks and the foreign keys hold the slices on a direct write;
/// company B neither sees nor schedules company A's order; the migration's Down refuses while a slice exists. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly PurchasingMutationAudit LineScheduleSet =
        new PurchasingMutationAudit("purchasing.purchase-order.line-schedule-set.v1", "Schedule", "SENTETIK termin dilimleri kaydedildi");

    private static readonly DateOnly ScheduleD1 = new(2026, 12, 1);
    private static readonly DateOnly ScheduleD2 = new(2027, 1, 15);

    private static PurchaseOrderDeliverySliceInput KgSlice(decimal quantity, DateOnly date) =>
        new(Guid.NewGuid(), Quantity.Of(quantity, QuantityUnit.Kilogram), date);

    [SqlServerFact]
    public async Task A_line_schedule_is_stored_as_an_append_only_revision_rebuilt_and_the_checks_hold_the_slices()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, approvedVersion) = await ApprovedAcceptanceOrderAsync(run, 100m);
        var first = new[] { KgSlice(60m, ScheduleD1), KgSlice(40m, ScheduleD2) };

        await using (StoreScope a = run.Store(CompanyA))
        {
            var set = await a.Store.MutateOrderAsync(run.Envelope("k-schedule"), Hash("k-schedule"), PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(approvedVersion, lineId, first, run.Actor, now), LineScheduleSet);
            Assert.Equal((approvedVersion + 1, 2), (set!.Order.Order.Version, set.Order.Order.Lines[0].DeliverySchedule.Count));
            var replay = await a.Store.MutateOrderAsync(run.Envelope("k-schedule", correlation: "SENTETIK-second"), Hash("k-schedule"),
                PurchasingCommands.OrderLineScheduleV1, orderId, (_, _) => throw new InvalidOperationException("a replay never mutates"), LineScheduleSet);
            Assert.Equal((PurchasingWriteOutcome.Replayed, run.Correlation), (replay!.Outcome, replay.OriginalCorrelationId));
        }

        long scheduledVersion = approvedVersion + 1;
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderLineDeliverySlices WHERE OrderId = @id AND LineId = @line AND OrderVersionAfter = @v",
            ("@id", orderId), ("@line", lineId), ("@v", scheduledVersion)));
        Assert.Equal(100m, await run.ScalarAsync<decimal>("SELECT SUM(Quantity) FROM purchasing.PurchaseOrderLineDeliverySlices WHERE OrderId = @id",
            ("@id", orderId)));
        Assert.Equal(scheduledVersion, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName = @command AND OrderId = @id",
            ("@command", PurchasingCommands.OrderLineScheduleV1), ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.line-schedule-set.v1' AND Operation = N'Schedule' "
            + "AND EntityId = @id AND EntityVersionAfter = @v", ("@id", orderId.ToString("D")), ("@v", scheduledVersion)));

        // A receipt in between, then a new revision: the earlier one stays as history, the rebuild interleaves both.
        var seed = new AcceptanceSeed(orderId, lineId, AcceptanceUnit, scheduledVersion);
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, Guid.NewGuid(), "30"),
            Envelope($"schedule-accept-{orderId:N}", $"correlation-schedule-accept-{orderId:N}"));
        long acceptedVersion = scheduledVersion + 1;
        // ALS-D81: the received 30 kg stay on the first slice (same id and date); only the unreceived rest is re-planned.
        var second = new[] { first[0] with { Quantity = Quantity.Of(30m, QuantityUnit.Kilogram) }, KgSlice(50m, ScheduleD2), KgSlice(20m, ScheduleD2) };
        await using (StoreScope a = run.Store(CompanyA))
        {
            // A domain refusal (Σ ≠ ordered) writes nothing.
            var refused = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateOrderAsync(run.Envelope("k-mismatch"), Hash("k-mismatch"),
                PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(acceptedVersion, lineId, [KgSlice(70m, ScheduleD1)], run.Actor, now), LineScheduleSet));
            Assert.Equal(PurchasingRuleCodes.ScheduleTotalMismatch, refused.Code);
            // ALS-D81: re-dating the received part is refused and writes nothing.
            var locked = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateOrderAsync(run.Envelope("k-locked"), Hash("k-locked"),
                PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(acceptedVersion, lineId, [KgSlice(100m, ScheduleD2)], run.Actor, now), LineScheduleSet));
            Assert.Equal(PurchasingRuleCodes.ScheduleReceivedPortionLocked, locked.Code);
            await a.Store.MutateOrderAsync(run.Envelope("k-schedule-2"), Hash("k-schedule-2"), PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(acceptedVersion, lineId, second, run.Actor, now), LineScheduleSet);
        }

        Assert.Equal(5, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLineDeliverySlices WHERE OrderId = @id", ("@id", orderId)));
        await using (StoreScope a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal((acceptedVersion + 1, 30m), (rebuilt.Version, rebuilt.Lines[0].AcceptedQuantity.Value));
            Assert.Equal(second.Select(slice => slice.SliceId).ToArray(), rebuilt.Lines[0].DeliverySchedule.Select(slice => slice.SliceId).ToArray());
            Assert.Equal([1, 2, 3], rebuilt.Lines[0].DeliverySchedule.Select(slice => slice.SliceNo).ToArray());
        }

        // The checks and keys refuse what the domain refuses, on a direct write too.
        foreach (var (sql, check) in new[]
                 {
                     ("UPDATE purchasing.PurchaseOrderLineDeliverySlices SET Quantity = 0 WHERE OrderId = @id AND SliceNo = 1", "CK_PurchaseOrderLineDeliverySlices_Quantity"),
                     ("UPDATE purchasing.PurchaseOrderLineDeliverySlices SET SliceNo = 501 WHERE OrderId = @id AND SliceNo = 3", "CK_PurchaseOrderLineDeliverySlices_SliceNo"),
                     ("UPDATE purchasing.PurchaseOrderLineDeliverySlices SET OrderVersionAfter = 1 WHERE OrderId = @id AND SliceNo = 3", "CK_PurchaseOrderLineDeliverySlices_Version"),
                     ("UPDATE purchasing.PurchaseOrderLineDeliverySlices SET RecordedBy = N'' WHERE OrderId = @id AND SliceNo = 1", "CK_PurchaseOrderLineDeliverySlices_Text"),
                     ("UPDATE purchasing.PurchaseOrderLineDeliverySlices SET SliceId = '00000000-0000-0000-0000-000000000000' WHERE OrderId = @id AND SliceNo = 1", "CK_PurchaseOrderLineDeliverySlices_Identity"),
                     ("UPDATE purchasing.PurchaseOrderLineDeliverySlices SET LineId = NEWID() WHERE OrderId = @id AND SliceNo = 1", "FK_PurchaseOrderLineDeliverySlices_PurchaseOrderLines_CompanyId_OrderId_LineId"),
                 })
        {
            var refusedWrite = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", orderId)));
            Assert.Equal(547, refusedWrite.Number);
            Assert.Contains(check, refusedWrite.Message, StringComparison.Ordinal);
        }

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE s SET SliceId = (SELECT SliceId FROM purchasing.PurchaseOrderLineDeliverySlices WHERE OrderId = @id AND OrderVersionAfter = s.OrderVersionAfter AND SliceNo = 1) "
            + "FROM purchasing.PurchaseOrderLineDeliverySlices s WHERE s.OrderId = @id AND s.SliceNo = 2 AND s.OrderVersionAfter = (SELECT MAX(OrderVersionAfter) FROM purchasing.PurchaseOrderLineDeliverySlices WHERE OrderId = @id)",
            ("@id", orderId)));
        Assert.Contains("UX_PurchaseOrderLineDeliverySlices_SliceId", duplicate.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task A_draft_line_is_scheduled_before_the_approval_and_company_B_neither_sees_nor_schedules_it()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        PurchaseOrder built = Order(owners, null, null, 5m);
        Guid orderId;
        Guid lineId = built.Lines[0].Id;
        await using (StoreScope a = run.Store(CompanyA))
        {
            orderId = (await a.Store.StoreOrderAsync(run.Envelope("k-draft"), Hash("draft"), owners.Account, Day, () => built, VersionsOf(built))).Order.Order.Id;
        }

        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindOrderAsync(orderId));
            Assert.Null(await b.Store.MutateOrderAsync(run.Envelope("k-b-schedule"), Hash("b-schedule"), PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(2, lineId, [KgSlice(5m, ScheduleD1)], run.Actor, now), LineScheduleSet));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLineDeliverySlices"));
        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.MutateOrderAsync(run.Envelope("k-draft-schedule"), Hash("draft-schedule"), PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(2, lineId, [KgSlice(2m, ScheduleD1), KgSlice(3m, ScheduleD2)], run.Actor, now), LineScheduleSet);
            await a.Store.MutateOrderAsync(run.Envelope("k-draft-approve", actor: Manager), Hash("draft-approve"), PurchasingCommands.OrderApproveV1, orderId,
                (order, now) => order.Approve(3, OrderApprover, now), Approved);
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal((PurchaseOrderStatus.Approved, 4L, 2), (rebuilt.Status, rebuilt.Version, rebuilt.Lines[0].DeliverySchedule.Count));
        }
    }

    [SqlServerFact]
    public async Task Two_schedules_of_one_line_saved_concurrently_yield_one_revision_and_the_loser_is_a_concurrent_change()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, approvedVersion) = await ApprovedAcceptanceOrderAsync(run, 100m);
        var setA = new[] { KgSlice(60m, ScheduleD1), KgSlice(40m, ScheduleD2) };
        var setB = new[] { KgSlice(100m, ScheduleD2) };

        Barrier barrier = new Barrier(2);
        async Task<object> ScheduleAsync(string key, PurchaseOrderDeliverySliceInput[] slices)
        {
            await using StoreScope writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                var result = await writer.Store.MutateOrderAsync(run.Envelope(key), Hash(key), PurchasingCommands.OrderLineScheduleV1, orderId,
                    (order, now) => order.SetLineDeliverySchedule(approvedVersion, lineId, slices, run.Actor, now), LineScheduleSet);
                return result!.Outcome;
            }
            catch (PurchasingStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(() => ScheduleAsync("k-race-a", setA)), Task.Run(() => ScheduleAsync("k-race-b", setB)));
        Assert.Single(results, result => result is PurchasingWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == PurchasingStoreCodes.ConcurrentChange);

        // One revision only, at the next version, and exactly one header bump and one audit record.
        Assert.Equal(approvedVersion + 1, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(DISTINCT OrderVersionAfter) FROM purchasing.PurchaseOrderLineDeliverySlices WHERE OrderId = @id", ("@id", orderId)));
        Assert.Equal(100m, await run.ScalarAsync<decimal>("SELECT SUM(Quantity) FROM purchasing.PurchaseOrderLineDeliverySlices WHERE OrderId = @id",
            ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.line-schedule-set.v1' AND EntityId = @id",
            ("@id", orderId.ToString("D"))));

        await using (StoreScope a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            var winner = results[0] is PurchasingWriteOutcome.Recorded ? setA : setB;
            Assert.Equal(winner.Select(slice => slice.SliceId).ToArray(), rebuilt.Lines[0].DeliverySchedule.Select(slice => slice.SliceId).ToArray());
        }
    }

    [SqlServerFact]
    public async Task The_line_schedule_migrations_Down_refuses_while_a_slice_exists()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, approvedVersion) = await ApprovedAcceptanceOrderAsync(run, 5m);
        var migration = new AddPurchaseOrderLineDeliverySchedule();
        string guardSql = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;

        // Nothing scheduled yet: the guard passes (and changes nothing).
        await run.ExecuteAsync(guardSql);

        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.MutateOrderAsync(run.Envelope("k-schedule"), Hash("schedule"), PurchasingCommands.OrderLineScheduleV1, orderId,
                (order, now) => order.SetLineDeliverySchedule(approvedVersion, lineId, [KgSlice(5m, ScheduleD1)], run.Actor, now), LineScheduleSet);
        }

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(51173, guard.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLineDeliverySlices WHERE OrderId = @id", ("@id", orderId)));
    }
}
