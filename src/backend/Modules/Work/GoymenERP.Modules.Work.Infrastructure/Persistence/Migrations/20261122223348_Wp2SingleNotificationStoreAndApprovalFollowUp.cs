using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Wp2SingleNotificationStoreAndApprovalFollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropIndex(
                name: "UX_WorkApprovalInboxEvents_Cycle",
                schema: "work",
                table: "ApprovalInboxEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Identity",
                schema: "work",
                table: "ApprovalInboxEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Shape",
                schema: "work",
                table: "ApprovalInboxEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Time",
                schema: "work",
                table: "ApprovalInboxEvents");

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                schema: "work",
                table: "InboxNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ObservedPurchaseOrderVersion",
                schema: "work",
                table: "InboxNotifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ObservedSalesOrderVersion",
                schema: "work",
                table: "InboxNotifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccurredAtUtc",
                schema: "work",
                table: "InboxNotifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderId",
                schema: "work",
                table: "InboxNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderLineId",
                schema: "work",
                table: "InboxNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                schema: "work",
                table: "InboxNotifications",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonText",
                schema: "work",
                table: "InboxNotifications",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesOrderId",
                schema: "work",
                table: "InboxNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesOrderLineId",
                schema: "work",
                table: "InboxNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEntityId",
                schema: "work",
                table: "InboxNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                schema: "work",
                table: "InboxNotifications",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "SourceVersion",
                schema: "work",
                table: "InboxNotifications",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalFollowUps",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    DocumentLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Link = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    ApproverPermission = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EscalationPermission = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RuleVersion = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    PolicyVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalFollowUps", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkApprovalFollowUps_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [ItemId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Kind]) > 0 AND [Step] IN (1, 2, 3, 4) AND LEN([DocumentLabel]) > 0 AND LEN([Link]) > 0 AND LEN([ApproverPermission]) > 0 AND LEN([RuleVersion]) > 0 AND LEN([CorrelationId]) > 0 AND [RecipientCount] >= 0 AND [RecordedAtUtc] >= [CycleStartedAtUtc] AND ([EscalationPermission] IS NULL OR LEN([EscalationPermission]) > 0) AND ([PolicyVersion] IS NULL OR LEN([PolicyVersion]) > 0) AND ([Step] NOT IN (2, 3) OR ([DueAtUtc] IS NOT NULL AND [PolicyVersion] IS NOT NULL AND [RecipientCount] >= 1)) AND ([Step] <> 3 OR [EscalationPermission] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ApprovalFollowUps_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_WorkInboxNotifications_EventGrain",
                schema: "work",
                table: "InboxNotifications",
                columns: new[] { "CompanyId", "SourceName", "EventId", "RecipientUserId", "SalesOrderId", "SalesOrderLineId", "PurchaseOrderId", "PurchaseOrderLineId" },
                unique: true,
                filter: "[EventId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Event",
                schema: "work",
                table: "InboxNotifications",
                sql: "([Kind] IN (4, 5) AND [SourceId] = [Id] AND [SourceName] IS NOT NULL AND LEN([SourceName]) > 0 AND [EventId] IS NOT NULL AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEntityId] IS NOT NULL AND [SourceEntityId] <> '00000000-0000-0000-0000-000000000000' AND [SourceVersion] IS NOT NULL AND LEN([SourceVersion]) > 0 AND [ReasonCode] IS NOT NULL AND LEN([ReasonCode]) > 0 AND [ReasonText] IS NOT NULL AND LEN([ReasonText]) > 0 AND [OccurredAtUtc] IS NOT NULL AND (([SalesOrderId] IS NOT NULL AND [SalesOrderLineId] IS NOT NULL AND [ObservedSalesOrderVersion] IS NOT NULL AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedSalesOrderVersion] >= 1 AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL) OR ([Kind] = 5 AND [PurchaseOrderId] IS NOT NULL AND [PurchaseOrderLineId] IS NOT NULL AND [ObservedPurchaseOrderVersion] IS NOT NULL AND [PurchaseOrderId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedPurchaseOrderVersion] >= 1 AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL))) OR ([Kind] NOT IN (4, 5) AND [SourceName] IS NULL AND [EventId] IS NULL AND [SourceEntityId] IS NULL AND [SourceVersion] IS NULL AND [ReasonCode] IS NULL AND [ReasonText] IS NULL AND [OccurredAtUtc] IS NULL AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrelationId]) > 0 AND (([Kind] = 1 AND [Class] = 1 AND [RecordType] IS NOT NULL AND [RecordId] IS NOT NULL AND [ActorUserId] IS NOT NULL) OR ([Kind] = 2 AND [Class] = 2 AND [ActorUserId] IS NOT NULL) OR ([Kind] = 3 AND [Class] = 1 AND [ActorUserId] IS NULL) OR ([Kind] IN (4, 5) AND [Class] = 2 AND [ActorUserId] IS NULL AND [RecordType] IS NULL AND [RecordId] IS NULL) OR ([Kind] IN (6, 7, 8, 9) AND [Class] = 1 AND [ActorUserId] IS NULL AND [RecordType] IS NULL AND [RecordId] IS NULL)) AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.CreateIndex(
                name: "UX_WorkApprovalInboxEvents_Cycle",
                schema: "work",
                table: "ApprovalInboxEvents",
                columns: new[] { "CompanyId", "Kind", "ItemId", "CycleStartedAtUtc", "EventType" },
                unique: true,
                filter: "[EventType] IN (1, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Identity",
                schema: "work",
                table: "ApprovalInboxEvents",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [ItemId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Kind]) > 0 AND [EventType] IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Shape",
                schema: "work",
                table: "ApprovalInboxEvents",
                sql: "([EventType] IN (1, 4, 5) AND [ClientEventId] IS NULL) OR ([EventType] IN (2, 3) AND [ClientEventId] IS NOT NULL AND [ClientEventId] <> '00000000-0000-0000-0000-000000000000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Time",
                schema: "work",
                table: "ApprovalInboxEvents",
                sql: "([EventType] <> 1 OR [OccurredAtUtc] = [CycleStartedAtUtc]) AND [RecordedAtUtc] >= [CycleStartedAtUtc] AND ([EventType] <> 5 OR [OccurredAtUtc] >= [CycleStartedAtUtc])");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalFollowUps_CompanyId_Step_RecordedAtUtc_Id",
                schema: "work",
                table: "ApprovalFollowUps",
                columns: new[] { "CompanyId", "Step", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_WorkApprovalFollowUps_CycleStep",
                schema: "work",
                table: "ApprovalFollowUps",
                columns: new[] { "CompanyId", "Kind", "ItemId", "CycleStartedAtUtc", "Step" },
                unique: true);

            // D9 (WP2): move every work.Notifications row into the single store with its own id (passivations keep pointing at
            // it), kind 1 -> 4 and 2 -> 5, "özet" class, its own id as source id. Nothing is dropped before the copy is checked.
            migrationBuilder.Sql("""
                INSERT INTO [work].[InboxNotifications] ([CompanyId], [Id], [RecipientUserId], [Kind], [Class], [SourceId],
                    [RecordType], [RecordId], [ActorUserId], [RecordedAtUtc], [CorrelationId], [CausationId], [SourceName], [EventId],
                    [SourceEntityId], [SourceVersion], [ReasonCode], [ReasonText], [OccurredAtUtc], [SalesOrderId], [SalesOrderLineId],
                    [ObservedSalesOrderVersion], [PurchaseOrderId], [PurchaseOrderLineId], [ObservedPurchaseOrderVersion])
                SELECT [CompanyId], [Id], [RecipientUserId], [Kind] + 3, 2, [Id], NULL, NULL, NULL, [RecordedAtUtc], [CorrelationId],
                    [CausationId], [SourceName], [EventId], [SourceEntityId], [SourceVersion], [ReasonCode], [ReasonText], [OccurredAtUtc],
                    [SalesOrderId], [SalesOrderLineId], [ObservedSalesOrderVersion], [PurchaseOrderId], [PurchaseOrderLineId],
                    [ObservedPurchaseOrderVersion]
                FROM [work].[Notifications];
                IF (SELECT COUNT_BIG(*) FROM [work].[Notifications])
                    <> (SELECT COUNT_BIG(*) FROM [work].[InboxNotifications] AS moved WHERE moved.[Kind] IN (4, 5)
                        AND EXISTS (SELECT 1 FROM [work].[Notifications] AS old WHERE old.[CompanyId] = moved.[CompanyId] AND old.[Id] = moved.[Id]))
                BEGIN
                    THROW 52111, 'work.Notifications rows were not all moved into work.InboxNotifications; Wp2SingleNotificationStoreAndApprovalFollowUp stops before dropping the old store.', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationPassivations_Notifications_CompanyId_NotificationId",
                schema: "work",
                table: "NotificationPassivations");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "work");

            migrationBuilder.AddForeignKey(
                name: "FK_InboxNotifications_NotificationInbox_CompanyId_SourceName_EventId",
                schema: "work",
                table: "InboxNotifications",
                columns: new[] { "CompanyId", "SourceName", "EventId" },
                principalSchema: "work",
                principalTable: "NotificationInbox",
                principalColumns: new[] { "CompanyId", "SourceName", "EventId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationPassivations_InboxNotifications_CompanyId_NotificationId",
                schema: "work",
                table: "NotificationPassivations",
                columns: new[] { "CompanyId", "NotificationId" },
                principalSchema: "work",
                principalTable: "InboxNotifications",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: the old store cannot take back the single store's rows without losing meaning (moved or new event
            // notifications, approval notifications, follow-up steps, owner decision events). An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[InboxNotifications] WHERE [Kind] >= 4)
                    OR EXISTS (SELECT 1 FROM [work].[ApprovalFollowUps])
                    OR EXISTS (SELECT 1 FROM [work].[ApprovalInboxEvents] WHERE [EventType] = 5)
                BEGIN
                    THROW 52110, 'work.InboxNotifications holds event or approval notifications, or approval follow-ups or owner decision events exist; Wp2SingleNotificationStoreAndApprovalFollowUp is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_InboxNotifications_NotificationInbox_CompanyId_SourceName_EventId",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationPassivations_InboxNotifications_CompanyId_NotificationId",
                schema: "work",
                table: "NotificationPassivations");

            migrationBuilder.DropTable(
                name: "ApprovalFollowUps",
                schema: "work");

            migrationBuilder.DropIndex(
                name: "UX_WorkInboxNotifications_EventGrain",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_Event",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropIndex(
                name: "UX_WorkApprovalInboxEvents_Cycle",
                schema: "work",
                table: "ApprovalInboxEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Identity",
                schema: "work",
                table: "ApprovalInboxEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Shape",
                schema: "work",
                table: "ApprovalInboxEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Time",
                schema: "work",
                table: "ApprovalInboxEvents");

            migrationBuilder.DropColumn(
                name: "EventId",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "ObservedPurchaseOrderVersion",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "ObservedSalesOrderVersion",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "OccurredAtUtc",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderLineId",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "ReasonText",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineId",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "SourceEntityId",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "SourceName",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                schema: "work",
                table: "InboxNotifications");

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ObservedPurchaseOrderVersion = table.Column<long>(type: "bigint", nullable: true),
                    ObservedSalesOrderVersion = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkNotifications_Evidence", "LEN([SourceVersion]) > 0 AND LEN([ReasonCode]) > 0 AND LEN([ReasonText]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_WorkNotifications_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceEntityId] <> '00000000-0000-0000-0000-000000000000' AND [Kind] IN (1, 2)");
                    table.CheckConstraint("CK_WorkNotifications_Target", "([SalesOrderId] IS NOT NULL AND [SalesOrderLineId] IS NOT NULL AND [ObservedSalesOrderVersion] IS NOT NULL AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedSalesOrderVersion] >= 1 AND [PurchaseOrderId] IS NULL AND [PurchaseOrderLineId] IS NULL AND [ObservedPurchaseOrderVersion] IS NULL) OR ([Kind] = 2 AND [PurchaseOrderId] IS NOT NULL AND [PurchaseOrderLineId] IS NOT NULL AND [ObservedPurchaseOrderVersion] IS NOT NULL AND [PurchaseOrderId] <> '00000000-0000-0000-0000-000000000000' AND [PurchaseOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [ObservedPurchaseOrderVersion] >= 1 AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL AND [ObservedSalesOrderVersion] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Notifications_NotificationInbox_CompanyId_SourceName_EventId",
                        columns: x => new { x.CompanyId, x.SourceName, x.EventId },
                        principalSchema: "work",
                        principalTable: "NotificationInbox",
                        principalColumns: new[] { "CompanyId", "SourceName", "EventId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkInboxNotifications_Identity",
                schema: "work",
                table: "InboxNotifications",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [RecipientUserId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrelationId]) > 0 AND (([Kind] = 1 AND [Class] = 1 AND [RecordType] IS NOT NULL AND [RecordId] IS NOT NULL AND [ActorUserId] IS NOT NULL) OR ([Kind] = 2 AND [Class] = 2 AND [ActorUserId] IS NOT NULL) OR ([Kind] = 3 AND [Class] = 1 AND [ActorUserId] IS NULL)) AND (([RecordType] IS NULL AND [RecordId] IS NULL) OR ([RecordType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12) AND [RecordId] IS NOT NULL AND [RecordId] <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.CreateIndex(
                name: "UX_WorkApprovalInboxEvents_Cycle",
                schema: "work",
                table: "ApprovalInboxEvents",
                columns: new[] { "CompanyId", "Kind", "ItemId", "CycleStartedAtUtc", "EventType" },
                unique: true,
                filter: "[EventType] IN (1, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Identity",
                schema: "work",
                table: "ApprovalInboxEvents",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [ItemId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Kind]) > 0 AND [EventType] IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Shape",
                schema: "work",
                table: "ApprovalInboxEvents",
                sql: "([EventType] IN (1, 4) AND [ClientEventId] IS NULL) OR ([EventType] IN (2, 3) AND [ClientEventId] IS NOT NULL AND [ClientEventId] <> '00000000-0000-0000-0000-000000000000')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkApprovalInboxEvents_Time",
                schema: "work",
                table: "ApprovalInboxEvents",
                sql: "([EventType] <> 1 OR [OccurredAtUtc] = [CycleStartedAtUtc]) AND [RecordedAtUtc] >= [CycleStartedAtUtc]");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CompanyId_RecipientUserId_RecordedAtUtc_Id",
                schema: "work",
                table: "Notifications",
                columns: new[] { "CompanyId", "RecipientUserId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CompanyId_SourceName_EventId_RecipientUserId_SalesOrderId_SalesOrderLineId_PurchaseOrderId_PurchaseOrderLineId",
                schema: "work",
                table: "Notifications",
                columns: new[] { "CompanyId", "SourceName", "EventId", "RecipientUserId", "SalesOrderId", "SalesOrderLineId", "PurchaseOrderId", "PurchaseOrderLineId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationPassivations_Notifications_CompanyId_NotificationId",
                schema: "work",
                table: "NotificationPassivations",
                columns: new[] { "CompanyId", "NotificationId" },
                principalSchema: "work",
                principalTable: "Notifications",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
