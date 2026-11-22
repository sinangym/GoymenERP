using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseGoodsReceiptBatchPieceFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseGoodsReceiptBatches_Variance",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatches");

            migrationBuilder.AddColumn<int>(
                name: "PieceCount",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchLabels",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KilogramsPerUnit",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatches",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseGoodsReceiptBatchLabels_PieceCount",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchLabels",
                sql: "[PieceCount] IS NULL OR [PieceCount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseGoodsReceiptBatches_Variance",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatches",
                sql: "[VarianceOutcome] IN (1, 2, 3, 4) AND [ExpectedQuantityKg] > 0 AND [ReceivedQuantityKg] > 0 AND ([TolerancePercent] IS NULL OR ([TolerancePercent] >= 0 AND [TolerancePercent] <= 100)) AND (([VarianceOutcome] = 4 AND [TolerancePercent] IS NULL) OR ([VarianceOutcome] IN (2, 3) AND [TolerancePercent] IS NOT NULL) OR [VarianceOutcome] = 1) AND (([KilogramsPerUnit] IS NULL AND ([VarianceOutcome] <> 2 OR [VarianceAcknowledged] = 1) AND ([Status] <> 2 OR [VarianceOutcome] IN (1, 2) OR [DecidedBy] IS NOT NULL)) OR ([KilogramsPerUnit] IS NOT NULL AND [KilogramsPerUnit] > 0 AND [Status] = 2 AND [DecidedBy] IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // AK-3b (KLN-D119, BS-16/BS-17): forward fix — a bulk receipt of a piece-unit line would lose its piece counts and its
            // recorded kg per piece, and a piece batch posted outside the tolerance would violate the earlier variance rule. The
            // rollback refuses while one exists; a store with kilogram batches only rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseGoodsReceiptBatches] WHERE [KilogramsPerUnit] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseGoodsReceiptBatchLabels] WHERE [PieceCount] IS NOT NULL)
                BEGIN
                    THROW 52042, 'Purchase bulk receipt piece facts exist; AddPurchaseGoodsReceiptBatchPieceFacts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseGoodsReceiptBatchLabels_PieceCount",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchLabels");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseGoodsReceiptBatches_Variance",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatches");

            migrationBuilder.DropColumn(
                name: "PieceCount",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatchLabels");

            migrationBuilder.DropColumn(
                name: "KilogramsPerUnit",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatches");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseGoodsReceiptBatches_Variance",
                schema: "purchasing",
                table: "PurchaseGoodsReceiptBatches",
                sql: "[VarianceOutcome] IN (1, 2, 3, 4) AND [ExpectedQuantityKg] > 0 AND [ReceivedQuantityKg] > 0 AND ([TolerancePercent] IS NULL OR ([TolerancePercent] >= 0 AND [TolerancePercent] <= 100)) AND (([VarianceOutcome] = 4 AND [TolerancePercent] IS NULL) OR ([VarianceOutcome] IN (2, 3) AND [TolerancePercent] IS NOT NULL) OR [VarianceOutcome] = 1) AND ([VarianceOutcome] <> 2 OR [VarianceAcknowledged] = 1) AND ([Status] <> 2 OR [VarianceOutcome] IN (1, 2) OR [DecidedBy] IS NOT NULL)");
        }
    }
}
