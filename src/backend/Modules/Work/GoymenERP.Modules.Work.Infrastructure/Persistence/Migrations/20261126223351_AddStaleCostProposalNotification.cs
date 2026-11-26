using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaleCostProposalNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_Event",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Event",
                schema: "work",
                table: "InboxNotifications",
                sql: "([Kind] IN (4, 5, 10) AND [SourceId] = [Id] AND [SourceName] IS NOT NULL AND LEN([SourceName]) > 0 AND [EventId] IS NOT NULL AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEntityId] IS NOT NULL AND [SourceEntityId] <> '00000000-0000-0000-0000-000000000000' AND [SourceVersion] IS NOT NULL AND LEN([SourceVersion]) > 0 AND [ReasonCode] IS NOT NULL AND LEN([ReasonCode]) > 0 AND [ReasonText] IS NOT NULL AND LEN([ReasonText]) > 0 AND [OccurredAtUtc] IS NOT NULL AND (([Kind] IN (4, 5) AND [SalesOrderId] IS NOT NULL AND [SalesOrderLineId] IS NOT NULL AND [ObservedSalesOrderVersion] IS NOT NULL AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedSalesOrderVersion] >= 1 AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL) OR ([Kind] = 5 AND [PurchaseOrderId] IS NOT NULL AND [PurchaseOrderLineId] IS NOT NULL AND [ObservedPurchaseOrderVersion] IS NOT NULL AND [PurchaseOrderId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedPurchaseOrderVersion] >= 1 AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL) OR ([Kind] = 10 AND [SourceName] = 'accounting.cost-proposals' AND [ReasonCode] = 'inventory_cost_preparation_stale' AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL))) OR ([Kind] NOT IN (4, 5, 10) AND [SourceName] IS NULL AND [EventId] IS NULL AND [SourceEntityId] IS NULL AND [SourceVersion] IS NULL AND [ReasonCode] IS NULL AND [ReasonText] IS NULL AND [OccurredAtUtc] IS NULL AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrelationId]) > 0 AND (([Kind] = 1 AND [Class] = 1 AND [RecordType] IS NOT NULL AND [RecordId] IS NOT NULL AND [ActorUserId] IS NOT NULL) OR ([Kind] = 2 AND [Class] = 2 AND [ActorUserId] IS NOT NULL) OR ([Kind] = 3 AND [Class] = 1 AND [ActorUserId] IS NULL) OR ([Kind] IN (4, 5, 10) AND [Class] = 2 AND [ActorUserId] IS NULL AND [RecordType] IS NULL AND [RecordId] IS NULL) OR ([Kind] IN (6, 7, 8, 9) AND [Class] = 1 AND [ActorUserId] IS NULL AND [RecordType] IS NULL AND [RecordId] IS NULL)) AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[InboxNotifications] WHERE [Kind] = 10)
                    THROW 52142, 'Stale cost notification evidence exists; forward fix required.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_Event",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Event",
                schema: "work",
                table: "InboxNotifications",
                sql: "([Kind] IN (4, 5) AND [SourceId] = [Id] AND [SourceName] IS NOT NULL AND LEN([SourceName]) > 0 AND [EventId] IS NOT NULL AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEntityId] IS NOT NULL AND [SourceEntityId] <> '00000000-0000-0000-0000-000000000000' AND [SourceVersion] IS NOT NULL AND LEN([SourceVersion]) > 0 AND [ReasonCode] IS NOT NULL AND LEN([ReasonCode]) > 0 AND [ReasonText] IS NOT NULL AND LEN([ReasonText]) > 0 AND [OccurredAtUtc] IS NOT NULL AND (([SalesOrderId] IS NOT NULL AND [SalesOrderLineId] IS NOT NULL AND [ObservedSalesOrderVersion] IS NOT NULL AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedSalesOrderVersion] >= 1 AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL) OR ([Kind] = 5 AND [PurchaseOrderId] IS NOT NULL AND [PurchaseOrderLineId] IS NOT NULL AND [ObservedPurchaseOrderVersion] IS NOT NULL AND [PurchaseOrderId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedPurchaseOrderVersion] >= 1 AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL))) OR ([Kind] NOT IN (4, 5) AND [SourceName] IS NULL AND [EventId] IS NULL AND [SourceEntityId] IS NULL AND [SourceVersion] IS NULL AND [ReasonCode] IS NULL AND [ReasonText] IS NULL AND [OccurredAtUtc] IS NULL AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrelationId]) > 0 AND (([Kind] = 1 AND [Class] = 1 AND [RecordType] IS NOT NULL AND [RecordId] IS NOT NULL AND [ActorUserId] IS NOT NULL) OR ([Kind] = 2 AND [Class] = 2 AND [ActorUserId] IS NOT NULL) OR ([Kind] = 3 AND [Class] = 1 AND [ActorUserId] IS NULL) OR ([Kind] IN (4, 5) AND [Class] = 2 AND [ActorUserId] IS NULL AND [RecordType] IS NULL AND [RecordId] IS NULL) OR ([Kind] IN (6, 7, 8, 9) AND [Class] = 1 AND [ActorUserId] IS NULL AND [RecordType] IS NULL AND [RecordId] IS NULL)) AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");
        }
    }
}
