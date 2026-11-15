using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// API-11 (ALS-D10/ALS-D12, F-017 §2/§3.6, ALS-D87): the short-delivery closure of a general purchase-order line. Adds the line
    /// share <c>Closed</c> (decimal(18,3), NOT NULL, 0 for every existing line — no line is closed before this migration), widens the
    /// shares check to <c>Technical + Accepted + Cancelled + Closed &lt;= Ordered</c> with <c>Closed &gt;= 0</c> and adds the
    /// closed-or-cancelled check (a remainder is never both). Creates the append-only <c>purchasing.PurchaseOrderLineClosureEvents</c>
    /// (one closure or reopening per (CompanyId, OrderId, OrderVersionAfter), bound to the order and to the line of the same company)
    /// and the transactional outbox <c>purchasing.OutboxMessages</c> (ADR-009 envelope; only <c>purchasing.order-line.closed.v1</c>;
    /// unique per company, type, aggregate and sequence). Nothing is backfilled and Up runs no SQL.
    /// <para>
    /// Down refuses (<c>THROW 51174</c>, used by no other migration) while a closure event, an outbox message or a closed share exists:
    /// dropping them would erase recorded closures, undelivered messages to the Inventory owner and the closed remainder that every
    /// rebuilt order version counts (forward fix). Otherwise it drops both tables and the column and restores the A1-3 shares check.
    /// </para>
    /// </summary>
    public partial class AddPurchaseOrderLineClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.AddColumn<decimal>(
                name: "Closed",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    SourceOperation = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboxMessages_Envelope", "LEN([EventType]) > 0 AND [SchemaVersion] >= 1 AND LEN([SourceOperation]) > 0 AND LEN([AggregateType]) > 0 AND [AggregateVersion] >= 1 AND [Sequence] >= 1 AND LEN([CorrelationId]) > 0 AND ISJSON([PayloadJson]) = 1");
                    table.CheckConstraint("CK_OutboxMessages_EventType", "[EventType] = 'purchasing.order-line.closed.v1'");
                    table.CheckConstraint("CK_OutboxMessages_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AggregateId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_OutboxMessages_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLineClosureEvents",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Closed = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLineClosureEvents", x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter });
                    table.CheckConstraint("CK_PurchaseOrderLineClosureEvents_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PurchaseOrderLineClosureEvents_Text", "LEN([Reason]) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PurchaseOrderLineClosureEvents_Version", "[OrderVersionAfter] >= 3");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLineClosureEvents_PurchaseOrderLines_CompanyId_OrderId_LineId",
                        columns: x => new { x.CompanyId, x.OrderId, x.LineId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrderLines",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLineClosureEvents_PurchaseOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_ClosedOrCancelled",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "[Closed] = 0 OR [Cancelled] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Cancelled] >= 0 AND [Closed] >= 0 AND [Technical] + [Accepted] + [Cancelled] + [Closed] <= [Ordered]");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CompanyId_RecordedAtUtc_Id",
                schema: "purchasing",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_OutboxMessages_AggregateSequence",
                schema: "purchasing",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "EventType", "AggregateId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLineClosureEvents_OrderLineVersion",
                schema: "purchasing",
                table: "PurchaseOrderLineClosureEvents",
                columns: new[] { "CompanyId", "OrderId", "LineId", "OrderVersionAfter" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderLineClosureEvents])
                    OR EXISTS (SELECT 1 FROM [purchasing].[OutboxMessages])
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderLines] WHERE [Closed] <> 0)
                BEGIN
                    THROW 51174, 'Purchase order line closures exist; AddPurchaseOrderLineClosure is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLineClosureEvents",
                schema: "purchasing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_ClosedOrCancelled",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "Closed",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Cancelled] >= 0 AND [Technical] + [Accepted] + [Cancelled] <= [Ordered]");
        }
    }
}
