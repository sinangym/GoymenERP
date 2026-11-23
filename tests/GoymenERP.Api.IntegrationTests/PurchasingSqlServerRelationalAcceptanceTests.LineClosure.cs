using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-11 (ALS-D10/ALS-D12, F-017 §2/§3.6, ALS-D87) on real SQL Server: after a partial receipt the short-delivery closure of an
/// approved general line stores the closed remainder, one append-only closure event at the order version it produced and one
/// <c>purchasing.order-line.closed.v1</c> outbox message in ONE save with the header version, the attempt and the audit; the same key
/// replays; the closed remainder leaves the future supply (the line is not listed, its answer shows closed and zero allocatable) and
/// the open-line port; the order is rebuilt closure by closure; the reopening gives the remainder back with a second event and a
/// second message (<c>closed</c> false); the checks hold the shares, their nonnegative bounds, the events and the messages on a
/// direct write (ALS-D92 permits historical closed and cancelled shares together); company B neither sees nor closes company
/// A's line; the migration's Down refuses while a closure exists. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly PurchasingMutationAudit LineClosed =
        new PurchasingMutationAudit("purchasing.purchase-order.line-closed.v1", "CloseLine", "SENTETIK satır kalanı kapatıldı");

    private static readonly PurchasingMutationAudit LineReopened =
        new PurchasingMutationAudit("purchasing.purchase-order.line-reopened.v1", "ReopenLine", "SENTETIK satır kapanışı geri alındı");

    [SqlServerFact]
    public async Task A_line_closure_after_a_partial_receipt_is_stored_with_its_event_and_outbox_message_and_leaves_the_future_supply()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, approvedVersion) = await FutureSupplyOrderAsync(run, 10m, DateOnly.FromDateTime(AcceptanceTime.UtcDateTime));
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(new AcceptanceSeed(orderId, lineId, AcceptanceUnit, approvedVersion), Guid.NewGuid(), "4"),
            Envelope($"closure-accept-{orderId:N}", $"correlation-closure-accept-{orderId:N}"));
        long acceptedVersion = approvedVersion + 1;
        long closedVersion = acceptedVersion + 1;

        await using (StoreScope a = run.Store(CompanyA))
        {
            var closed = await a.Store.MutateOrderAsync(run.Envelope("k-close"), Hash("k-close"), PurchasingCommands.OrderLineCloseV1, orderId,
                (order, now) => order.CloseLine(acceptedVersion, lineId, run.Actor, "SENTETIK tedarikçi kalanı gönderemiyor", now), LineClosed);
            Assert.Equal((closedVersion, 6m, true), (closed!.Order.Order.Version, closed.Order.Order.Lines[0].ClosedQuantity.Value,
                closed.Order.Order.Lines[0].IsClosed));
            var replay = await a.Store.MutateOrderAsync(run.Envelope("k-close", correlation: "SENTETIK-second"), Hash("k-close"),
                PurchasingCommands.OrderLineCloseV1, orderId, (_, _) => throw new InvalidOperationException("a replay never mutates"), LineClosed);
            Assert.Equal((PurchasingWriteOutcome.Replayed, run.Correlation), (replay!.Outcome, replay.OriginalCorrelationId));
        }

        Assert.Equal((4m, 0m, 6m), (
            await run.ScalarAsync<decimal>("SELECT Accepted FROM purchasing.PurchaseOrderLines WHERE OrderId = @id AND LineId = @line", ("@id", orderId), ("@line", lineId)),
            await run.ScalarAsync<decimal>("SELECT Cancelled FROM purchasing.PurchaseOrderLines WHERE OrderId = @id AND LineId = @line", ("@id", orderId), ("@line", lineId)),
            await run.ScalarAsync<decimal>("SELECT Closed FROM purchasing.PurchaseOrderLines WHERE OrderId = @id AND LineId = @line", ("@id", orderId), ("@line", lineId))));
        Assert.Equal(closedVersion, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderLineClosureEvents WHERE OrderId = @id AND LineId = @line AND OrderVersionAfter = @v "
            + "AND Closed = 1 AND RecordedBy = @actor AND Reason = N'SENTETIK tedarikçi kalanı gönderemiyor' AND CorrelationId = @correlation",
            ("@id", orderId), ("@line", lineId), ("@v", closedVersion), ("@actor", run.Actor), ("@correlation", run.Correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.OutboxMessages WHERE CompanyId = @company AND EventType = @type AND SchemaVersion = 1 "
            + "AND SourceOperation = N'CloseLine' AND AggregateType = @aggregate AND AggregateId = @line AND AggregateVersion = @v AND Sequence = @v "
            + "AND CorrelationId = @correlation",
            ("@company", CompanyA), ("@type", PurchasingOrderLineClosedEventV1.EventType), ("@aggregate", PurchasingOrderLineClosedEventV1.AggregateType),
            ("@line", lineId), ("@v", closedVersion), ("@correlation", run.Correlation)));
        Assert.Equal((CompanyA.ToString("D"), orderId.ToString("D"), lineId.ToString("D"), "true", closedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), (
            await run.ScalarAsync<string>("SELECT JSON_VALUE(PayloadJson, '$.companyId') FROM purchasing.OutboxMessages WHERE AggregateId = @line", ("@line", lineId)),
            await run.ScalarAsync<string>("SELECT JSON_VALUE(PayloadJson, '$.purchaseOrderId') FROM purchasing.OutboxMessages WHERE AggregateId = @line", ("@line", lineId)),
            await run.ScalarAsync<string>("SELECT JSON_VALUE(PayloadJson, '$.purchaseOrderLineId') FROM purchasing.OutboxMessages WHERE AggregateId = @line", ("@line", lineId)),
            await run.ScalarAsync<string>("SELECT JSON_VALUE(PayloadJson, '$.closed') FROM purchasing.OutboxMessages WHERE AggregateId = @line", ("@line", lineId)),
            await run.ScalarAsync<string>("SELECT JSON_VALUE(PayloadJson, '$.version') FROM purchasing.OutboxMessages WHERE AggregateId = @line", ("@line", lineId))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName = @command AND OrderId = @id",
            ("@command", PurchasingCommands.OrderLineCloseV1), ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.line-closed.v1' AND Operation = N'CloseLine' "
            + "AND EntityId = @id AND EntityVersionAfter = @v", ("@id", orderId.ToString("D")), ("@v", closedVersion)));

        // The closed remainder leaves the future supply: the answer shows it closed with nothing allocatable and the list skips the line.
        var answer = await ReadFutureSupplyLineAsync(run, CompanyA, orderId, lineId);
        Assert.Equal(PurchaseFutureSupplyOutcomeV1.Allocatable, answer.Outcome);
        var supplied = Assert.IsType<PurchaseFutureSupplyLineV1>(answer.Line);
        Assert.Equal(("10", "4", "0", "6", "0", closedVersion), (supplied.CommittedText, supplied.AcceptedText, supplied.CancelledText,
            supplied.ClosedText, supplied.AllocatableText, supplied.ObservedOrderVersion));
        var page = await ReadFutureSupplyPageAsync(run, CompanyA, AcceptanceProduct, PurchaseFutureSupplyPageV1.MaximumPageSize, null);
        Assert.DoesNotContain(page.Items, item => item.OrderId == orderId);

        // ... and the open-line port (Lojistik/Stok readers).
        Assert.DoesNotContain((await OpenApprovedLinesAsync(run, CompanyA)).Items, item => item.OrderId == orderId);

        // The order is rebuilt with its closure.
        await using (StoreScope a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal((closedVersion, 6m, 0m), (rebuilt.Version, rebuilt.Lines[0].ClosedQuantity.Value, rebuilt.Lines[0].RemainingQuantity.Value));
            Assert.Equal((run.Actor, "SENTETIK tedarikçi kalanı gönderemiyor"), (rebuilt.Lines[0].Closure!.ClosedBy, rebuilt.Lines[0].Closure!.Reason));

            // A second closure of a closed line is the domain's refusal and writes nothing.
            var again = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateOrderAsync(run.Envelope("k-close-again"), Hash("k-close-again"),
                PurchasingCommands.OrderLineCloseV1, orderId, (order, now) => order.CloseLine(closedVersion, lineId, run.Actor, "SENTETIK", now), LineClosed));
            Assert.Equal(PurchasingRuleCodes.LineAlreadyClosed, again.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLineClosureEvents WHERE OrderId = @id", ("@id", orderId)));

        // The reopening gives the remainder back: a second event and a second message (closed false), the earlier ones stay.
        long reopenedVersion = closedVersion + 1;
        await using (StoreScope a = run.Store(CompanyA))
        {
            var reopened = await a.Store.MutateOrderAsync(run.Envelope("k-reopen"), Hash("k-reopen"), PurchasingCommands.OrderLineReopenV1, orderId,
                (order, now) => order.ReopenLine(closedVersion, lineId, run.Actor, "SENTETIK tedarikçi kalanı gönderecek", now), LineReopened);
            Assert.Equal((reopenedVersion, 0m, false), (reopened!.Order.Order.Version, reopened.Order.Order.Lines[0].ClosedQuantity.Value,
                reopened.Order.Order.Lines[0].IsClosed));
        }

        Assert.Equal(0m, await run.ScalarAsync<decimal>("SELECT Closed FROM purchasing.PurchaseOrderLines WHERE OrderId = @id AND LineId = @line",
            ("@id", orderId), ("@line", lineId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLineClosureEvents WHERE OrderId = @id", ("@id", orderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseOrderLineClosureEvents WHERE OrderId = @id AND OrderVersionAfter = @v AND Closed = 0",
            ("@id", orderId), ("@v", reopenedVersion)));
        Assert.Equal("false", await run.ScalarAsync<string>(
            "SELECT JSON_VALUE(PayloadJson, '$.closed') FROM purchasing.OutboxMessages WHERE AggregateId = @line AND Sequence = @v AND SourceOperation = N'ReopenLine'",
            ("@line", lineId), ("@v", reopenedVersion)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages WHERE AggregateId = @line", ("@line", lineId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-order.line-reopened.v1' AND Operation = N'ReopenLine' "
            + "AND EntityId = @id AND EntityVersionAfter = @v", ("@id", orderId.ToString("D")), ("@v", reopenedVersion)));
        var reopenedSupply = Assert.IsType<PurchaseFutureSupplyLineV1>((await ReadFutureSupplyLineAsync(run, CompanyA, orderId, lineId)).Line);
        Assert.Equal(("0", "6"), (reopenedSupply.ClosedText, reopenedSupply.AllocatableText));
        Assert.Equal("6", Assert.Single((await OpenApprovedLinesAsync(run, CompanyA)).Items, item => item.OrderId == orderId).RemainingText);

        await using (StoreScope a = run.Store(CompanyA))
        {
            var rebuilt = (await a.Store.FindOrderAsync(orderId))!.Order;
            Assert.Equal((reopenedVersion, 0m, 6m), (rebuilt.Version, rebuilt.Lines[0].ClosedQuantity.Value, rebuilt.Lines[0].RemainingQuantity.Value));
            Assert.Null(rebuilt.Lines[0].Closure);
        }
    }

    [SqlServerFact]
    public async Task The_line_closure_checks_hold_the_shares_the_events_and_the_messages_and_company_B_neither_sees_nor_closes_the_line()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, approvedVersion) = await ApprovedAcceptanceOrderAsync(run, 10m);

        // Company B neither finds nor closes company A's line; nothing is written.
        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.MutateOrderAsync(run.Envelope("k-close-b"), Hash("k-close-b"), PurchasingCommands.OrderLineCloseV1, orderId,
                (order, now) => order.CloseLine(approvedVersion, lineId, run.Actor, "SENTETIK", now), LineClosed));
        }

        Assert.Equal(0m, await run.ScalarAsync<decimal>("SELECT Closed FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLineClosureEvents WHERE OrderId = @id", ("@id", orderId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.OutboxMessages WHERE AggregateId = @line", ("@line", lineId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName = @command AND OrderId = @id",
            ("@command", PurchasingCommands.OrderLineCloseV1), ("@id", orderId)));

        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.MutateOrderAsync(run.Envelope("k-close"), Hash("k-close"), PurchasingCommands.OrderLineCloseV1, orderId,
                (order, now) => order.CloseLine(approvedVersion, lineId, run.Actor, "SENTETIK", now), LineClosed);
        }

        const string readLine = "SELECT * FROM purchasing.PurchaseOrderLines WHERE CompanyId = @company AND OrderId = @id AND LineId = @line FOR JSON PATH, WITHOUT_ARRAY_WRAPPER";
        var originalRow = await run.ScalarAsync<string>(readLine, ("@company", CompanyA), ("@id", orderId), ("@line", lineId));
        var originalClosed = await run.ScalarAsync<decimal>(
            "SELECT Closed FROM purchasing.PurchaseOrderLines WHERE CompanyId = @company AND OrderId = @id AND LineId = @line",
            ("@company", CompanyA), ("@id", orderId), ("@line", lineId));
        var originalCancelled = await run.ScalarAsync<decimal>(
            "SELECT Cancelled FROM purchasing.PurchaseOrderLines WHERE CompanyId = @company AND OrderId = @id AND LineId = @line",
            ("@company", CompanyA), ("@id", orderId), ("@line", lineId));
        var negativeClosed = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseOrderLines SET Closed = -1 WHERE CompanyId = @company AND OrderId = @id AND LineId = @line",
            ("@company", CompanyA), ("@id", orderId), ("@line", lineId)));
        Assert.Equal(547, negativeClosed.Number);
        // Both real CHECKs forbid a negative closed share; SQL does not guarantee which overlapping CHECK reports first.
        Assert.True(negativeClosed.Message.Contains("CK_PurchaseOrderLines_Shares", StringComparison.Ordinal)
            || negativeClosed.Message.Contains("CK_PurchaseOrderLines_ClosedOrCancelled", StringComparison.Ordinal));
        Assert.Equal(originalRow, await run.ScalarAsync<string>(readLine,
            ("@company", CompanyA), ("@id", orderId), ("@line", lineId)));

        // ALS-D92: a receipt revival can leave historical closed and cancelled shares together, within the original total.
        try
        {
            await run.ExecuteAsync(
                "UPDATE purchasing.PurchaseOrderLines SET Closed = 5, Cancelled = 5 WHERE CompanyId = @company AND OrderId = @id AND LineId = @line",
                ("@company", CompanyA), ("@id", orderId), ("@line", lineId));
            Assert.Equal(5m, await run.ScalarAsync<decimal>(
                "SELECT Closed FROM purchasing.PurchaseOrderLines WHERE CompanyId = @company AND OrderId = @id AND LineId = @line",
                ("@company", CompanyA), ("@id", orderId), ("@line", lineId)));
            Assert.Equal(5m, await run.ScalarAsync<decimal>(
                "SELECT Cancelled FROM purchasing.PurchaseOrderLines WHERE CompanyId = @company AND OrderId = @id AND LineId = @line",
                ("@company", CompanyA), ("@id", orderId), ("@line", lineId)));
        }
        finally
        {
            await run.ExecuteAsync(
                "UPDATE purchasing.PurchaseOrderLines SET Closed = @closed, Cancelled = @cancelled WHERE CompanyId = @company AND OrderId = @id AND LineId = @line",
                ("@closed", originalClosed), ("@cancelled", originalCancelled), ("@company", CompanyA), ("@id", orderId), ("@line", lineId));
        }
        Assert.Equal(originalRow, await run.ScalarAsync<string>(readLine,
            ("@company", CompanyA), ("@id", orderId), ("@line", lineId)));

        // The checks refuse what the domain refuses, on a direct write too.
        foreach (var (sql, check) in new[]
                 {
                     ("UPDATE purchasing.PurchaseOrderLines SET Closed = 11 WHERE OrderId = @id", "CK_PurchaseOrderLines_Shares"),
                     ("UPDATE purchasing.PurchaseOrderLineClosureEvents SET OrderVersionAfter = 2 WHERE OrderId = @id", "CK_PurchaseOrderLineClosureEvents_Version"),
                     ("UPDATE purchasing.PurchaseOrderLineClosureEvents SET Reason = N'' WHERE OrderId = @id", "CK_PurchaseOrderLineClosureEvents_Text"),
                     ("UPDATE purchasing.PurchaseOrderLineClosureEvents SET LineId = '00000000-0000-0000-0000-000000000000' WHERE OrderId = @id", "CK_PurchaseOrderLineClosureEvents_Identity"),
                     ("UPDATE purchasing.PurchaseOrderLineClosureEvents SET LineId = NEWID() WHERE OrderId = @id", "FK_PurchaseOrderLineClosureEvents_PurchaseOrderLines_CompanyId_OrderId_LineId"),
                     ("UPDATE purchasing.OutboxMessages SET EventType = 'purchasing.unknown.v1' WHERE AggregateId = @line", "CK_OutboxMessages_EventType"),
                     ("UPDATE purchasing.OutboxMessages SET PayloadJson = N'not json' WHERE AggregateId = @line", "CK_OutboxMessages_Envelope"),
                     ("UPDATE purchasing.OutboxMessages SET Sequence = 0 WHERE AggregateId = @line", "CK_OutboxMessages_Envelope"),
                     ("UPDATE purchasing.OutboxMessages SET AggregateId = '00000000-0000-0000-0000-000000000000' WHERE AggregateId = @line", "CK_OutboxMessages_Identity"),
                 })
        {
            var refusedWrite = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(sql, ("@id", orderId), ("@line", lineId)));
            Assert.Equal(547, refusedWrite.Number);
            Assert.Contains(check, refusedWrite.Message, StringComparison.Ordinal);
        }

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO purchasing.OutboxMessages (Id, CompanyId, EventType, SchemaVersion, SourceOperation, AggregateType, AggregateId, AggregateVersion, "
            + "Sequence, PayloadJson, OccurredAtUtc, CorrelationId, CausationId, RecordedAtUtc) "
            + "SELECT NEWID(), CompanyId, EventType, SchemaVersion, SourceOperation, AggregateType, AggregateId, AggregateVersion, Sequence, PayloadJson, "
            + "OccurredAtUtc, CorrelationId, CausationId, RecordedAtUtc FROM purchasing.OutboxMessages WHERE AggregateId = @line",
            ("@line", lineId)));
        Assert.Contains("UX_OutboxMessages_AggregateSequence", duplicate.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task The_line_closure_migrations_Down_refuses_while_a_closure_exists()
    {
        await using Run run = await Run.StartAsync();
        var (orderId, lineId, approvedVersion) = await ApprovedAcceptanceOrderAsync(run, 5m);
        var migration = new AddPurchaseOrderLineClosure();
        string guardSql = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;

        // Nothing closed yet: the guard passes (and changes nothing).
        await run.ExecuteAsync(guardSql);

        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.MutateOrderAsync(run.Envelope("k-close"), Hash("close"), PurchasingCommands.OrderLineCloseV1, orderId,
                (order, now) => order.CloseLine(approvedVersion, lineId, run.Actor, "SENTETIK", now), LineClosed);
        }

        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guardSql));
        Assert.Equal(51174, guard.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLineClosureEvents WHERE OrderId = @id", ("@id", orderId)));
        Assert.Equal(5m, await run.ScalarAsync<decimal>("SELECT Closed FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)));
    }

    private static async Task<PurchaseOpenLinePageV1> OpenApprovedLinesAsync(Run run, Guid companyId)
    {
        CompanyContext company = new CompanyContext();
        company.Set(companyId);
        await using PurchasingDbContext context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .Options, company);
        return await new PurchaseOpenLineReadPort(context, company).ListOpenApprovedLinesAsync(100, null);
    }
}
