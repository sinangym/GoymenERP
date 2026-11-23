using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

// WP2 (D9, YOL-D17, YOL-D28, D10) real-SQL evidence: the single notification store migration moves the retired rows without
// loss, its Down refuses while they exist, and the follow-up/decision constraints hold on SQL Server. SENTETIK data only.
public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    private const string BeforeWp2 = "20261122201654_AddWorkApprovalRuleVersions";

    [SqlServerFact]
    public async Task The_WP2_migration_moves_old_notifications_with_their_ids_and_passivations_and_Down_then_refuses()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var database = Db(connectionString, TestCompanyIds.CompanyA);
            var migrator = database.GetService<IMigrator>();
            // An empty single store rolls back freely to the old two-store schema.
            await migrator.MigrateAsync(BeforeWp2);
            var notificationId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            await ExecAsync(connectionString, $"""
                INSERT INTO [work].[NotificationInbox] ([CompanyId],[SourceName],[EventId],[EventType],[SchemaVersion],[PayloadSha256],[RecordedAtUtc])
                VALUES ('{TestCompanyIds.CompanyA}','inventory.notifications','{eventId}','inventory.future-allocation.released.v1',1,REPLICATE('a',64),'2026-11-17T00:00:00+00:00');
                INSERT INTO [work].[Notifications] ([CompanyId],[Id],[SourceName],[EventId],[Kind],[RecipientUserId],[SalesOrderId],[SalesOrderLineId],
                    [ObservedSalesOrderVersion],[SourceEntityId],[SourceVersion],[ReasonCode],[ReasonText],[OccurredAtUtc],[RecordedAtUtc],[CorrelationId])
                VALUES ('{TestCompanyIds.CompanyA}','{notificationId}','inventory.notifications','{eventId}',1,'{Guid.NewGuid()}','{Guid.NewGuid()}',
                    '{Guid.NewGuid()}',3,'{Guid.NewGuid()}','2','acceptance-link-failed',N'SENTETIK ayrım bırakıldı','2026-11-17T00:00:00+00:00',
                    '2026-11-17T00:00:00+00:00','SENTETIK-move');
                INSERT INTO [work].[NotificationPassivations] ([CompanyId],[NotificationId],[PassivatedAtUtc],[ActiveDays],[PolicyVersion])
                VALUES ('{TestCompanyIds.CompanyA}','{notificationId}','2026-11-20T00:00:00+00:00',3,'work.notification-active-days:1');
                """);

            await migrator.MigrateAsync();

            Assert.Equal("4|2|1", await ScalarAsync(connectionString, $"""
                SELECT CONCAT([Kind], '|', [Class], '|', CASE WHEN [SourceId] = [Id] AND [EventId] = '{eventId}' THEN 1 ELSE 0 END)
                FROM [work].[InboxNotifications] WHERE [Id] = '{notificationId}'
                """));
            Assert.Equal(1, await CountAsync(connectionString, "NotificationPassivations", TestCompanyIds.CompanyA));
            Assert.True(string.IsNullOrEmpty(await ScalarAsync(connectionString, "SELECT OBJECT_ID(N'[work].[Notifications]')")));
            var guard = Assert.IsType<SqlOperation>(new Wp2SingleNotificationStoreAndApprovalFollowUp().DownOperations[0]).Sql;
            var refusal = await Assert.ThrowsAsync<SqlException>(() => database.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(52110, refusal.Number);
            Assert.Equal(1, await CountAsync(connectionString, "InboxNotifications", TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task Approval_follow_up_rows_are_one_per_cycle_and_step_and_a_reminder_needs_a_due_time_and_a_recipient()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var itemId = Guid.NewGuid();
            string Row(int step, string due, int recipients, string? escalation = "'work.approvals.escalate'") => $"""
                INSERT INTO [work].[ApprovalFollowUps] ([CompanyId],[Id],[Kind],[ItemId],[CycleStartedAtUtc],[Step],[DocumentLabel],[Link],
                    [ApproverPermission],[EscalationPermission],[RuleVersion],[PolicyVersion],[DueAtUtc],[RecipientCount],[RecordedAtUtc],[CorrelationId])
                VALUES ('{TestCompanyIds.CompanyA}','{Guid.NewGuid()}','logistics.dispatch-note.v1','{itemId}','2026-11-23T06:00:00+00:00',{step},
                    N'SENTETIK irsaliye','/lojistik/yukleme-talimatlari/{itemId}','logistics.dispatch-notes.approve',{escalation ?? "NULL"},
                    'work.approval-rule:logistics.dispatch-note.v1:0',{(due == "NULL" ? "NULL" : "'work.approval-duration-hours:0'")},{due},{recipients},
                    '2026-11-23T10:01:00+00:00','SENTETIK-follow-up');
                """;
            await ExecAsync(connectionString, Row(1, "NULL", 1));
            var duplicate = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, Row(1, "NULL", 1)));
            Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
            var noDue = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, Row(2, "NULL", 1)));
            Assert.Equal(547, noDue.Number);
            var nobody = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, Row(2, "'2026-11-23T10:00:00+00:00'", 0)));
            Assert.Equal(547, nobody.Number);
            var noPermission = await Assert.ThrowsAsync<SqlException>(() =>
                ExecAsync(connectionString, Row(3, "'2026-11-23T10:00:00+00:00'", 1, escalation: null)));
            Assert.Equal(547, noPermission.Number);
            await ExecAsync(connectionString, Row(2, "'2026-11-23T10:00:00+00:00'", 1));
            Assert.Equal(2, await CountAsync(connectionString, "ApprovalFollowUps", TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task An_owner_decision_event_is_one_per_cycle_and_never_before_the_cycle_began()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var itemId = Guid.NewGuid();
            string Decided(string occurred) => $"""
                INSERT INTO [work].[ApprovalInboxEvents] ([CompanyId],[Id],[Kind],[ItemId],[CycleStartedAtUtc],[EventType],[OccurredAtUtc],[RecordedAtUtc],[ClientEventId])
                VALUES ('{TestCompanyIds.CompanyA}','{Guid.NewGuid()}','logistics.dispatch-note.v1','{itemId}','2026-11-23T06:00:00+00:00',5,
                    '{occurred}','2026-11-23T08:00:00+00:00',NULL);
                """;
            var early = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, Decided("2026-11-23T05:59:00+00:00")));
            Assert.Equal(547, early.Number);
            await ExecAsync(connectionString, Decided("2026-11-23T06:37:00+00:00"));
            var second = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, Decided("2026-11-23T06:40:00+00:00")));
            Assert.Contains(second.Number, new[] { 2601, 2627 });
        });
    }
}
