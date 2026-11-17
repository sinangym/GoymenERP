using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiptAdjustmentBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_OrderId_OrderVersionAfter",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_EventType",
                schema: "purchasing",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BatchLineNo",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptAdjustmentBatchId",
                schema: "purchasing",
                table: "PurchaseAttempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Position",
                schema: "purchasing",
                table: "OutboxMessages",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptAdjustmentBatches",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceiptAdjustmentBatches", x => new { x.CompanyId, x.BatchId });
                    table.UniqueConstraint("AK_PurchaseReceiptAdjustmentBatches_CompanyId_OrderId_BatchId", x => new { x.CompanyId, x.OrderId, x.BatchId });
                    table.CheckConstraint("CK_PurchaseReceiptAdjustmentBatches_Facts", "[OrderVersionAfter] >= 2 AND [LineCount] BETWEEN 1 AND 500 AND LEN([RecordedBy]) > 0 AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
                    table.CheckConstraint("CK_PurchaseReceiptAdjustmentBatches_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [BatchId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptAdjustmentBatches_PurchaseOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_BatchId_BatchLineNo",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                columns: new[] { "CompanyId", "BatchId", "BatchLineNo" },
                unique: true,
                filter: "[BatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_OrderId_BatchId",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                columns: new[] { "CompanyId", "OrderId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_OrderId_OrderVersionAfter",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                columns: new[] { "CompanyId", "OrderId", "OrderVersionAfter" },
                unique: true,
                filter: "[BatchId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseReceiptAdjustments_Batch",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                sql: "([BatchId] IS NULL AND [BatchLineNo] IS NULL) OR ([BatchId] IS NOT NULL AND [BatchId] <> '00000000-0000-0000-0000-000000000000' AND [BatchLineNo] IS NOT NULL AND [BatchLineNo] BETWEEN 1 AND 500)");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseAttempts_CompanyId_OrderId_ReceiptAdjustmentBatchId",
                schema: "purchasing",
                table: "PurchaseAttempts",
                columns: new[] { "CompanyId", "OrderId", "ReceiptAdjustmentBatchId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseAttempts_ReceiptAdjustmentBatch",
                schema: "purchasing",
                table: "PurchaseAttempts",
                sql: "[ReceiptAdjustmentBatchId] IS NULL OR ([ReceiptAdjustmentBatchId] <> '00000000-0000-0000-0000-000000000000' AND [ReceiptAdjustmentId] IS NULL AND [Outcome] = 1 AND [OrderId] IS NOT NULL AND [RequestId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Position",
                schema: "purchasing",
                table: "OutboxMessages",
                column: "Position");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_EventType",
                schema: "purchasing",
                table: "OutboxMessages",
                sql: "[EventType] IN ('purchasing.order-line.closed.v1', 'purchasing.order-line.revived.v1')");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAdjustmentBatches_CompanyId_OrderId_OrderVersionAfter",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustmentBatches",
                columns: new[] { "CompanyId", "OrderId", "OrderVersionAfter" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseAttempts_PurchaseReceiptAdjustmentBatches_CompanyId_OrderId_ReceiptAdjustmentBatchId",
                schema: "purchasing",
                table: "PurchaseAttempts",
                columns: new[] { "CompanyId", "OrderId", "ReceiptAdjustmentBatchId" },
                principalSchema: "purchasing",
                principalTable: "PurchaseReceiptAdjustmentBatches",
                principalColumns: new[] { "CompanyId", "OrderId", "BatchId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReceiptAdjustments_PurchaseReceiptAdjustmentBatches_CompanyId_OrderId_BatchId",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                columns: new[] { "CompanyId", "OrderId", "BatchId" },
                principalSchema: "purchasing",
                principalTable: "PurchaseReceiptAdjustmentBatches",
                principalColumns: new[] { "CompanyId", "OrderId", "BatchId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Batch identity/replay and the outbox cursor are durable history. Removing them
            // would lose the original grouped decision or re-publish already observed events.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseReceiptAdjustmentBatches])
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseReceiptAdjustments] WHERE [BatchId] IS NOT NULL OR [BatchLineNo] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseAttempts] WHERE [ReceiptAdjustmentBatchId] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [purchasing].[OutboxMessages])
                BEGIN
                    THROW 51956, 'Receipt adjustment batch or outbox cursor history exists; AddPurchaseReceiptAdjustmentBatches is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseAttempts_PurchaseReceiptAdjustmentBatches_CompanyId_OrderId_ReceiptAdjustmentBatchId",
                schema: "purchasing",
                table: "PurchaseAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReceiptAdjustments_PurchaseReceiptAdjustmentBatches_CompanyId_OrderId_BatchId",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments");

            migrationBuilder.DropTable(
                name: "PurchaseReceiptAdjustmentBatches",
                schema: "purchasing");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_BatchId_BatchLineNo",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_OrderId_BatchId",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_OrderId_OrderVersionAfter",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseReceiptAdjustments_Batch",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseAttempts_CompanyId_OrderId_ReceiptAdjustmentBatchId",
                schema: "purchasing",
                table: "PurchaseAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseAttempts_ReceiptAdjustmentBatch",
                schema: "purchasing",
                table: "PurchaseAttempts");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Position",
                schema: "purchasing",
                table: "OutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OutboxMessages_EventType",
                schema: "purchasing",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "BatchId",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments");

            migrationBuilder.DropColumn(
                name: "BatchLineNo",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments");

            migrationBuilder.DropColumn(
                name: "ReceiptAdjustmentBatchId",
                schema: "purchasing",
                table: "PurchaseAttempts");

            migrationBuilder.DropColumn(
                name: "Position",
                schema: "purchasing",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAdjustments_CompanyId_OrderId_OrderVersionAfter",
                schema: "purchasing",
                table: "PurchaseReceiptAdjustments",
                columns: new[] { "CompanyId", "OrderId", "OrderVersionAfter" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OutboxMessages_EventType",
                schema: "purchasing",
                table: "OutboxMessages",
                sql: "[EventType] = 'purchasing.order-line.closed.v1'");
        }
    }
}
