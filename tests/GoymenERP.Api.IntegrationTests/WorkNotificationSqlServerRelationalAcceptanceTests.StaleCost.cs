using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Stale_cost_event_is_one_preparer_digest_notification_and_one_inbox_receipt_on_replay()
    {
        // Real consumer + WP2 SQL store; the Accounting owner envelope is explicitly synthetic here.
        await WithDatabaseAsync(async connectionString =>
        {
            await using var provider = NotificationServices(connectionString);
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var consumer = new AccountingCostProposalStaleNotificationConsumerV1(scope.ServiceProvider.GetRequiredService<IWorkNotificationDeliveryV1>());
            var message = AccountingCostProposalStaleConsumerTests.Envelope();
            provider.GetRequiredService<NotificationRecipient>().Outcome = WorkNotificationRecipientOutcomeV1.Unavailable;
            provider.GetRequiredService<NotificationPurchaseRecipients>().Outcome = WorkNotificationRecipientOutcomeV1.Unavailable;
            await consumer.HandleAsync(message, default); await consumer.HandleAsync(message, default);
            await using var database = Db(connectionString, TestCompanyIds.CompanyA);
            Assert.Equal(1, await database.NotificationInbox.CountAsync());
            var row = Assert.Single(await database.InboxNotifications.ToArrayAsync());
            var payload = System.Text.Json.JsonSerializer.Deserialize<GoymenERP.Modules.Accounting.Application.AccountingCostProposalStalePayloadV1>(
                message.PayloadJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            Assert.Equal(payload.PreparedBy, row.RecipientUserId); Assert.Equal((int)payload.CostKind, row.CostKind);
            Assert.Equal((10, 2), (row.Kind, row.Class)); Assert.Equal("Bayatladı", row.ReasonText);
            Assert.Null(row.SalesOrderId); Assert.Null(row.PurchaseOrderId);
            provider.GetRequiredService<NotificationUser>().Id = payload.PreparedBy;
            var own = Assert.Single((await scope.ServiceProvider.GetRequiredService<IWorkNotificationReadV1>().ListMineAsync(false)).Items);
            Assert.Equal("accounting-cost-proposal-stale", own.Kind); Assert.Equal((int)payload.CostKind, own.CostKind);
            provider.GetRequiredService<NotificationUser>().Id = Guid.NewGuid();
            Assert.Empty((await scope.ServiceProvider.GetRequiredService<IWorkNotificationReadV1>().ListMineAsync(false)).Items);
            Assert.Equal(0, await CountAsync(connectionString, "InboxNotifications", TestCompanyIds.CompanyB));
            var changed = message with { PayloadJson = message.PayloadJson.Replace(payload.PreparedBy.ToString(), Guid.NewGuid().ToString()) };
            Assert.Equal("notification_event_payload_mismatch", (await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => consumer.HandleAsync(changed, default))).Code);
            Assert.Equal(1, await database.InboxNotifications.CountAsync());
            var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
                new GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations.AddStaleCostProposalNotification().DownOperations[0]).Sql;
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
            await connection.OpenAsync(); await using var rollback = new Microsoft.Data.SqlClient.SqlCommand(guard, connection);
            Assert.Equal(52142, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => rollback.ExecuteNonQueryAsync())).Number);
        });
    }
    [SqlServerFact]
    public async Task Cost_kind_check_refuses_missing_unknown_and_other_notification_kind_annotations_on_real_sql()
    {
        // Actual Work SQL checks; the owner event and Sales recipient answer are explicit synthetic fixtures.
        await WithDatabaseAsync(async connectionString =>
        {
            await using var provider = NotificationServices(connectionString);
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var delivery = scope.ServiceProvider.GetRequiredService<IWorkNotificationDeliveryV1>();
            var allocation = NotificationCommand(); await delivery.DeliverAsync(allocation);
            await Assert.ThrowsAsync<OutboxPermanentFailureException>(() => delivery.DeliverAsync(allocation with { EventId = Guid.NewGuid(), CostKind = 1 }));
            var consumer = new AccountingCostProposalStaleNotificationConsumerV1(delivery);
            await consumer.HandleAsync(AccountingCostProposalStaleConsumerTests.Envelope(2), default);
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString); await connection.OpenAsync();
            foreach (var sql in new[] {
                "UPDATE [work].[InboxNotifications] SET [CostKind] = 1 WHERE [Kind] = 4",
                "UPDATE [work].[InboxNotifications] SET [CostKind] = NULL WHERE [Kind] = 10",
                "UPDATE [work].[InboxNotifications] SET [CostKind] = 3 WHERE [Kind] = 10" })
            {
                await using var command = new Microsoft.Data.SqlClient.SqlCommand(sql, connection);
                var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => command.ExecuteNonQueryAsync());
                Assert.Equal(547, error.Number); Assert.Contains("CK_WorkInboxNotifications_CostKind", error.Message);
            }
            await using var database = Db(connectionString, TestCompanyIds.CompanyA);
            var rows = await database.InboxNotifications.ToArrayAsync();
            Assert.Null(Assert.Single(rows, row => row.Kind == 4).CostKind);
            Assert.Equal(2, Assert.Single(rows, row => row.Kind == 10).CostKind);
            var down = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
                new GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations.AddStaleCostNotificationCostKind().DownOperations[0]).Sql;
            await using var rollback = new Microsoft.Data.SqlClient.SqlCommand(down, connection);
            Assert.Equal(52143, (await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => rollback.ExecuteNonQueryAsync())).Number);
        });
    }

}
