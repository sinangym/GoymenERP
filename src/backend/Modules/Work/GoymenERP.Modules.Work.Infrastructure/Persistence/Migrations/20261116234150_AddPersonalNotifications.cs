using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationInbox",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationInbox", x => new { x.CompanyId, x.SourceName, x.EventId });
                    table.CheckConstraint("CK_WorkNotificationInbox_Hash", "DATALENGTH([PayloadSha256]) = 64 AND [PayloadSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_WorkNotificationInbox_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND [SchemaVersion] >= 1 AND LEN([SourceName]) > 0 AND LEN([EventType]) > 0");
                    table.ForeignKey(
                        name: "FK_NotificationInbox_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "work",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObservedSalesOrderVersion = table.Column<long>(type: "bigint", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObservedPurchaseOrderVersion = table.Column<long>(type: "bigint", nullable: true),
                    SourceEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ReasonText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "NotificationPassivations",
                schema: "work",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PassivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActiveDays = table.Column<int>(type: "int", nullable: false),
                    PolicyVersion = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPassivations", x => new { x.CompanyId, x.NotificationId });
                    table.CheckConstraint("CK_WorkNotificationPassivations_Policy", "[ActiveDays] >= 0 AND LEN([PolicyVersion]) > 0");
                    table.ForeignKey(
                        name: "FK_NotificationPassivations_Notifications_CompanyId_NotificationId",
                        columns: x => new { x.CompanyId, x.NotificationId },
                        principalSchema: "work",
                        principalTable: "Notifications",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [work].[NotificationInbox])
                    OR EXISTS (SELECT 1 FROM [work].[Notifications])
                    OR EXISTS (SELECT 1 FROM [work].[NotificationPassivations])
                    THROW 51955, 'Notification delivery, deduplication or passivation history exists; use a forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "NotificationPassivations",
                schema: "work");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "work");

            migrationBuilder.DropTable(
                name: "NotificationInbox",
                schema: "work");
        }
    }
}
