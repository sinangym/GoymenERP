using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ALC-S16 (F-027; KLN-D41/KLN-D43/KLN-D45): the bulk goods receipt of a kg purchase-order line. Creates
    /// <c>purchasing.PurchaseGoodsReceiptBatches</c> (one batch per order line: the total received kg against the line's remaining
    /// kg, the variance outcome and the tolerance it was evaluated with, the preparer and the four-eyes decision of an
    /// outside-tolerance or tolerance-not-set batch), <c>purchasing.PurchaseGoodsReceiptBatchLabels</c> (one row per label: our label
    /// code, its source, the never-reused generated number, the original supplier label number in its own searchable column and the
    /// acceptance/lot/label/movement it posted; one unreleased code per company) and the idempotency ledger
    /// <c>purchasing.PurchaseGoodsReceiptBatchAttempts</c>. Every row carries <c>CompanyId</c> and is bound to its batch, order and
    /// line of the same company. Nothing is backfilled and Up runs no SQL.
    /// <para>
    /// Down refuses (<c>THROW 51616</c>, used by no other migration) while a batch or a recorded attempt exists: dropping them would
    /// erase posted receipts' label codes and supplier numbers, pending variance approvals and the replay ledger of the command
    /// (forward fix). Otherwise it drops the three tables.
    /// </para>
    /// </summary>
    public partial class AddPurchaseGoodsReceiptBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseGoodsReceiptBatches",
                schema: "purchasing",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ExpectedQuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReceivedQuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    VarianceOutcome = table.Column<int>(type: "int", nullable: false),
                    TolerancePercent = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    VarianceAcknowledged = table.Column<bool>(type: "bit", nullable: false),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseGoodsReceiptBatches", x => x.BatchId);
                    table.UniqueConstraint("AK_PurchaseGoodsReceiptBatches_CompanyId_BatchId", x => new { x.CompanyId, x.BatchId });
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatches_Decision", "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) OR ([Status] = 2 AND [DecisionReason] IS NULL AND (([DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL) OR ([DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [PreparedBy] COLLATE Latin1_General_100_BIN2))) OR ([Status] = 3 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NOT NULL AND LEN([DecisionReason]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [PreparedBy] COLLATE Latin1_General_100_BIN2)");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatches_Identity", "[BatchId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OrderId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [UnitId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ProductVersion]) > 0");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatches_Status", "[Status] IN (1, 2, 3)");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatches_Text", "LEN([PreparedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatches_Variance", "[VarianceOutcome] IN (1, 2, 3, 4) AND [ExpectedQuantityKg] > 0 AND [ReceivedQuantityKg] > 0 AND ([TolerancePercent] IS NULL OR ([TolerancePercent] >= 0 AND [TolerancePercent] <= 100)) AND (([VarianceOutcome] = 4 AND [TolerancePercent] IS NULL) OR ([VarianceOutcome] IN (2, 3) AND [TolerancePercent] IS NOT NULL) OR [VarianceOutcome] = 1) AND ([VarianceOutcome] <> 2 OR [VarianceAcknowledged] = 1) AND ([Status] <> 2 OR [VarianceOutcome] IN (1, 2) OR [DecidedBy] IS NOT NULL)");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatches_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_PurchaseGoodsReceiptBatches_PurchaseOrderLines_CompanyId_OrderId_LineId",
                        columns: x => new { x.CompanyId, x.OrderId, x.LineId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrderLines",
                        principalColumns: new[] { "CompanyId", "OrderId", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseGoodsReceiptBatches_PurchaseOrders_CompanyId_OrderId",
                        columns: x => new { x.CompanyId, x.OrderId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseGoodsReceiptBatchAttempts",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseGoodsReceiptBatchAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatchAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatchAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatchAttempts_Outcome", "([Outcome] = 1 AND [RefusalJson] IS NULL AND [BatchId] IS NOT NULL AND [BatchId] <> '00000000-0000-0000-0000-000000000000') OR ([Outcome] = 2 AND [BatchId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_PurchaseGoodsReceiptBatchAttempts_PurchaseGoodsReceiptBatches_CompanyId_BatchId",
                        columns: x => new { x.CompanyId, x.BatchId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseGoodsReceiptBatches",
                        principalColumns: new[] { "CompanyId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseGoodsReceiptBatchLabels",
                schema: "purchasing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CodeSource = table.Column<int>(type: "int", nullable: false),
                    GeneratedNumber = table.Column<long>(type: "bigint", nullable: true),
                    SupplierLabelNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MeasuredQuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    HeatNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CoilNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Released = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseGoodsReceiptBatchLabels", x => new { x.CompanyId, x.BatchId, x.LineNo });
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatchLabels_Code", "LEN([LabelCode]) > 0 AND (([CodeSource] = 3 AND [GeneratedNumber] IS NOT NULL AND [GeneratedNumber] >= 1) OR ([CodeSource] = 2 AND [GeneratedNumber] IS NULL) OR ([CodeSource] = 1 AND [GeneratedNumber] IS NULL AND [SupplierLabelNumber] IS NOT NULL AND [SupplierLabelNumber] COLLATE Latin1_General_100_BIN2 = [LabelCode] COLLATE Latin1_General_100_BIN2))");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatchLabels_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [BatchId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [LotId] <> '00000000-0000-0000-0000-000000000000' AND [LabelId] <> '00000000-0000-0000-0000-000000000000' AND [MovementId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatchLabels_LineNo", "[LineNo] >= 1");
                    table.CheckConstraint("CK_PurchaseGoodsReceiptBatchLabels_Quantity", "[MeasuredQuantityKg] > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseGoodsReceiptBatchLabels_PurchaseGoodsReceiptBatches_CompanyId_BatchId",
                        columns: x => new { x.CompanyId, x.BatchId },
                        principalSchema: "purchasing",
                        principalTable: "PurchaseGoodsReceiptBatches",
                        principalColumns: new[] { "CompanyId", "BatchId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceiptBatchAttempts_BatchId",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchAttempts",
                columns: new[] { "CompanyId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceiptBatches_CompanyId_OrderId_LineId",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatches",
                columns: new[] { "CompanyId", "OrderId", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceiptBatches_Order",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatches",
                columns: new[] { "CompanyId", "OrderId", "PreparedAtUtc", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceiptBatches_Status",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatches",
                columns: new[] { "CompanyId", "Status", "PreparedAtUtc", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceiptBatchLabels_GeneratedNumber",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchLabels",
                columns: new[] { "CompanyId", "GeneratedNumber" },
                filter: "[GeneratedNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGoodsReceiptBatchLabels_SupplierLabelNumber",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchLabels",
                columns: new[] { "CompanyId", "SupplierLabelNumber" },
                filter: "[SupplierLabelNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseGoodsReceiptBatchLabels_AcceptanceId",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchLabels",
                columns: new[] { "CompanyId", "AcceptanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseGoodsReceiptBatchLabels_LabelCode",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchLabels",
                columns: new[] { "CompanyId", "LabelCode" },
                unique: true,
                filter: "[Released] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseGoodsReceiptBatchLabels_LabelId",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchLabels",
                columns: new[] { "CompanyId", "LabelId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseGoodsReceiptBatches])
                    OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseGoodsReceiptBatchAttempts])
                BEGIN
                    THROW 51616, 'Purchase goods receipt batches exist; AddPurchaseGoodsReceiptBatches is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PurchaseGoodsReceiptBatchAttempts",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseGoodsReceiptBatchLabels",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseGoodsReceiptBatches",
                schema: "purchasing");
        }
    }
}
