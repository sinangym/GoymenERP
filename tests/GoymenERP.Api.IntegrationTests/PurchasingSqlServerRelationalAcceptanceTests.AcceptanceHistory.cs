using System.Data.Common;
using System.Reflection;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly DateTimeOffset AcceptanceHistoryTime = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Order_detail_reopens_the_same_order_with_ordered_immutable_acceptance_history_and_no_read_effect()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        StoredPurchaseOrder approved;
        await using (var scope = run.Store(CompanyA))
        {
            PurchaseOrder order = Order(owners, null, null, 100m);
            var stored = await scope.Store.StoreOrderAsync(
                run.Envelope("history-order-create"), Hash("history-order-create"), owners.Account, Day,
                () => order, VersionsOf(order));
            var transitioned = await scope.Store.MutateOrderAsync(
                run.Envelope("history-order-approve"), Hash("history-order-approve"), PurchasingCommands.OrderApproveV1,
                order.Id, (candidate, now) => candidate.Approve(2, now), Approved);
            approved = Assert.IsType<StoredPurchaseOrder>(transitioned?.Order);
        }

        Guid orderId = approved.Order.Id;
        Guid lineId = Assert.Single(approved.Order.Lines).Id;
        Guid firstId = Guid.Parse("64000000-0000-0000-0000-000000000001");
        Guid secondId = Guid.Parse("64000000-0000-0000-0000-000000000002");

        await CommitAcceptanceAsync(run, new PurchaseAcceptanceStageRequestV1(
            CompanyA, firstId, orderId, lineId, "3", Product, "7", KgUnit, "40", AcceptanceHistoryTime),
            "history-accept-40");

        await using (var firstScope = run.Store(CompanyA))
        {
            StoredPurchaseOrder first = Assert.IsType<StoredPurchaseOrder>(await firstScope.Store.FindOrderAsync(orderId));
            PurchaseOrderDetailView detail = Detail(first);
            Assert.Equal(("4", "40", "60"),
                (detail.Order.Version, Assert.Single(detail.Lines).Accepted, Assert.Single(detail.Lines).Remaining));
            var acceptance = Assert.Single(detail.Acceptances);
            Assert.Equal((firstId, lineId, "40", AcceptanceHistoryTime),
                (acceptance.AcceptanceId, acceptance.LineId, acceptance.Quantity, acceptance.RecordedAtUtc));
        }

        await CommitAcceptanceAsync(run, new PurchaseAcceptanceStageRequestV1(
            CompanyA, secondId, orderId, lineId, "4", Product, "7", KgUnit, "25", AcceptanceHistoryTime.AddMinutes(1)),
            "history-accept-25");

        var beforeReads = await AcceptanceHistoryStateAsync(run, orderId);
        await using (var reopenedScope = run.Store(CompanyA))
        {
            StoredPurchaseOrder reopened = Assert.IsType<StoredPurchaseOrder>(await reopenedScope.Store.FindOrderAsync(orderId));
            PurchaseOrderDetailView detail = Detail(reopened);
            Assert.Equal(("5", "65", "35"),
                (detail.Order.Version, Assert.Single(detail.Lines).Accepted, Assert.Single(detail.Lines).Remaining));
            Assert.Equal([firstId, secondId], detail.Acceptances.Select(item => item.AcceptanceId).ToArray());
            Assert.Equal(["40", "25"], detail.Acceptances.Select(item => item.Quantity).ToArray());
            Assert.Equal([AcceptanceHistoryTime, AcceptanceHistoryTime.AddMinutes(1)],
                detail.Acceptances.Select(item => item.RecordedAtUtc).ToArray());
        }

        await using (var repeatedScope = run.Store(CompanyA))
        {
            Assert.NotNull(await repeatedScope.Store.FindOrderAsync(orderId));
            Assert.NotNull(await repeatedScope.Store.FindOrderAsync(orderId));
            Assert.Null(await repeatedScope.Store.FindOrderAsync(Guid.NewGuid()));
        }
        await using (var foreignScope = run.Store(CompanyB))
        {
            Assert.Null(await foreignScope.Store.FindOrderAsync(orderId));
        }

        Assert.Equal(beforeReads, await AcceptanceHistoryStateAsync(run, orderId));
        Assert.Equal(
            ["AcceptanceId", "LineId", "Quantity", "RecordedAtUtc"],
            typeof(PurchaseOrderAcceptanceHistoryView).GetProperties().Select(property => property.Name).ToArray());
    }

    private static async Task CommitAcceptanceAsync(Run run, PurchaseAcceptanceStageRequestV1 request, string key)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using (var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA))
        {
            PurchaseAcceptanceStageResultV1 result = await participant.StageAsync(request, run.Envelope(key));
            Assert.False(result.IsReplay);
        }
        await transaction.CommitAsync();
    }

    private static PurchaseOrderDetailView Detail(StoredPurchaseOrder stored)
    {
        MethodInfo method = typeof(PurchasingService).GetMethod("OrderDetail", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Purchase order detail mapper bulunamadı.");
        return (PurchaseOrderDetailView)(method.Invoke(null, [stored])
            ?? throw new InvalidOperationException("Purchase order detail mapper sonuç döndürmedi."));
    }

    private static async Task<AcceptanceHistoryState> AcceptanceHistoryStateAsync(Run run, Guid orderId) => new(
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", orderId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseOrderLines WHERE OrderId = @id", ("@id", orderId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAcceptances WHERE OrderId = @id", ("@id", orderId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", orderId.ToString("D"))),
        await SchemaRowsAsync(run, "purchasing"),
        await SchemaRowsAsync(run, "inventory"),
        await SchemaRowsAsync(run, "finance"),
        await SchemaRowsAsync(run, "logistics"));

    private static Task<long> SchemaRowsAsync(Run run, string schema) => run.ScalarAsync<long>(
        "SELECT COALESCE(SUM(partition.rows), 0) FROM sys.tables tableRow "
        + "JOIN sys.schemas schemaRow ON schemaRow.schema_id = tableRow.schema_id "
        + "JOIN sys.partitions partition ON partition.object_id = tableRow.object_id AND partition.index_id IN (0, 1) "
        + "WHERE schemaRow.name = @schema",
        ("@schema", schema));

    private sealed record AcceptanceHistoryState(
        int Orders, int Lines, int Acceptances, int Attempts, int Audits,
        long PurchasingRows, long InventoryRows, long FinanceRows, long LogisticsRows);
}
