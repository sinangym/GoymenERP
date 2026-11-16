using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiptAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_Cancellation",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_ClosedOrCancelled",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.AddColumn<long>(
                name: "ExpectedPurchaseOrderVersion",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UndoQuantity",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefinitiveReturned",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptClosureRevived",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptAdjustmentId",
                schema: "purchasing",
                table: "PurchaseAttempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PurchaseAcceptanceShares_CompanyId_OrderId_LineId_AcceptanceId",
                schema: "purchasing",
                table: "PurchaseAcceptanceShares",
                columns: new[] { "CompanyId", "OrderId", "LineId", "AcceptanceId" });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderCancellationHistory",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderCancellationHistory", x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter });
                    table.CheckConstraint("CK_PurchaseOrderCancellationHistory_Facts", "[OrderVersionAfter] >= 2 AND LEN([RecordedBy]) > 0 AND LEN([Reason]) > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderCancellationHistory_PurchaseOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptAdjustments",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UndoQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    IsSupplierReturn = table.Column<bool>(type: "bit", nullable: false),
                    ReplacementExpected = table.Column<bool>(type: "bit", nullable: false),
                    NewAcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewAcceptedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    OrderVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    Revived = table.Column<bool>(type: "bit", nullable: false),
                    NetOpenedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceiptAdjustments", x => new { x.CompanyId, x.AdjustmentId });
                    table.CheckConstraint("CK_PurchaseReceiptAdjustments_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AdjustmentId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PurchaseReceiptAdjustments_Quantities", "[UndoQuantity] > 0 AND [NewAcceptedQuantity] >= 0 AND [NetOpenedQuantity] >= 0");
                    table.CheckConstraint("CK_PurchaseReceiptAdjustments_Replacement", "([NewAcceptanceId] IS NULL AND [NewAcceptedQuantity] = 0) OR ([NewAcceptanceId] IS NOT NULL AND [NewAcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [NewAcceptedQuantity] > 0 AND [IsSupplierReturn] = 0)");
                    table.CheckConstraint("CK_PurchaseReceiptAdjustments_Text", "[OrderVersionAfter] >= 2 AND LEN([RecordedBy]) > 0 AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptAdjustments_PurchaseAcceptanceShares_CompanyId_NewAcceptanceId",
                        columns: x => new { x.CompanyId, x.NewAcceptanceId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseAcceptanceShares",
                        principalColumns: new[] { "CompanyId", "AcceptanceId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptAdjustments_PurchaseAcceptanceShares_CompanyId_OrderId_LineId_AcceptanceId",
                        columns: x => new { x.CompanyId, x.OrderId, x.LineId, x.AcceptanceId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseAcceptanceShares",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId", "AcceptanceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseSupplierReturns_AdjustmentMetadata",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                sql: "([ExpectedPurchaseOrderVersion] IS NULL AND [UndoQuantity] IS NULL) OR ([ExpectedPurchaseOrderVersion] IS NOT NULL AND [ExpectedPurchaseOrderVersion] >= 1 AND [UndoQuantity] IS NOT NULL AND [UndoQuantity] > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_Cancellation",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "([Status] = 3 AND [CancelledBy] IS NOT NULL AND LEN([CancelledBy]) > 0 AND [CancelledAtUtc] IS NOT NULL AND [CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0) OR ([Status] = 2 AND [CancelledBy] IS NOT NULL AND LEN([CancelledBy]) > 0 AND [CancelledAtUtc] IS NOT NULL AND [CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0) OR ([Status] <> 3 AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_ClosedOrCancelled",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "[Closed] >= 0 AND [Cancelled] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "[Ordered] > 0 AND [Technical] >= 0 AND [Accepted] >= 0 AND [Cancelled] >= 0 AND [Closed] >= 0 AND [DefinitiveReturned] >= 0 AND [Technical] + [Accepted] + [Cancelled] + [Closed] + [DefinitiveReturned] <= [Ordered]");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseAttempts_CompanyId_ReceiptAdjustmentId",
                schema: "purchasing",
                table: "PurchaseAttempts",
                columns: new[] { "CompanyId", "ReceiptAdjustmentId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseAttempts_ReceiptAdjustment",
                schema: "purchasing",
                table: "PurchaseAttempts",
                sql: "[ReceiptAdjustmentId] IS NULL OR ([ReceiptAdjustmentId] <> '00000000-0000-0000-0000-000000000000' AND [Outcome] = 1 AND [OrderId] IS NOT NULL AND [RequestId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_NewAcceptanceId",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                columns: new[] { "CompanyId", "NewAcceptanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_OrderId_LineId_AcceptanceId",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                columns: new[] { "CompanyId", "OrderId", "LineId", "AcceptanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_OrderId_OrderVersionAfter",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                columns: new[] { "CompanyId", "OrderId", "OrderVersionAfter" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseAttempts_PurchaseReceiptAdjustments_CompanyId_ReceiptAdjustmentId",
                schema: "purchasing",
                table: "PurchaseAttempts",
                columns: new[] { "CompanyId", "ReceiptAdjustmentId" },
                principalSchema: "purchasing",
                principalTable: "PurchaseReceiptAdjustments",
                principalColumns: new[] { "CompanyId", "AdjustmentId" },
                onDelete: ReferentialAction.Restrict);

            // Forward fix: copy recorded cancellation facts unchanged; never invent an actor or a time.
            // K3 review pending: bounded owner-table data backfill required before history-based restoration.
            migrationBuilder.Sql("""
                INSERT INTO [purchasing].[PurchaseOrderCancellationHistory]
                    ([CompanyId], [OrderId], [OrderVersionAfter], [RecordedBy], [Reason], [RecordedAtUtc])
                SELECT [CompanyId], [Id], [Version], [CancelledBy], [CancellationReason], [CancelledAtUtc]
                FROM [purchasing].[PurchaseOrders]
                WHERE [Status] = 3;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseReceiptAdjustments])
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderCancellationHistory])
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderLines]
                        WHERE [DefinitiveReturned] <> 0 OR [ReceiptClosureRevived] = 1)
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseAttempts] WHERE [ReceiptAdjustmentId] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseSupplierReturns]
                        WHERE [ExpectedPurchaseOrderVersion] IS NOT NULL OR [UndoQuantity] IS NOT NULL)
                    THROW 51949, 'Receipt adjustment or cancellation history exists; use an approved forward fix.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseAttempts_PurchaseReceiptAdjustments_CompanyId_ReceiptAdjustmentId",
                schema: "purchasing",
                table: "PurchaseAttempts");

            migrationBuilder.DropTable(
                name: "PurchaseOrderCancellationHistory",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseReceiptAdjustments",
                schema: "purchasing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseSupplierReturns_AdjustmentMetadata",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_Cancellation",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_ClosedOrCancelled",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_Shares",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseAttempts_CompanyId_ReceiptAdjustmentId",
                schema: "purchasing",
                table: "PurchaseAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseAttempts_ReceiptAdjustment",
                schema: "purchasing",
                table: "PurchaseAttempts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PurchaseAcceptanceShares_CompanyId_OrderId_LineId_AcceptanceId",
                schema: "purchasing",
                table: "PurchaseAcceptanceShares");

            migrationBuilder.DropColumn(
                name: "ExpectedPurchaseOrderVersion",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.DropColumn(
                name: "UndoQuantity",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.DropColumn(
                name: "DefinitiveReturned",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "ReceiptClosureRevived",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "ReceiptAdjustmentId",
                schema: "purchasing",
                table: "PurchaseAttempts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_Cancellation",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "([Status] = 3 AND [CancelledBy] IS NOT NULL AND LEN([CancelledBy]) > 0 AND [CancelledAtUtc] IS NOT NULL AND [CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0) OR ([Status] <> 3 AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL)");

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
        }
    }
}
