using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// KLN-D119 (AK-2b): nullable piece counts on transfer buckets, transfer receipts (dispatched/received/shortage/excess),
    /// excess re-requests and stock scraps, so the remaining Inventory movements carry a piece-tracking label's pieces.
    /// Pure DDL; existing rows stay null (no backfill). Down is a forward fix: it refuses (THROW 52031) while any of these
    /// rows carries a piece count, because dropping the columns would silently lose the piece facts (a later acceptance of a
    /// dispatched label would restock it without pieces); an empty or piece-less store rolls back freely.
    /// </summary>
    public partial class AddInventoryMovementPieceCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DispatchedPieceCount",
                schema: "inventory",
                table: "TransferReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OveragePieceCount",
                schema: "inventory",
                table: "TransferReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceivedPieceCount",
                schema: "inventory",
                table: "TransferReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShortagePieceCount",
                schema: "inventory",
                table: "TransferReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OveragePieceCount",
                schema: "inventory",
                table: "TransferOverageRenewals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PieceCount",
                schema: "inventory",
                table: "TransferBuckets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BookPieceCount",
                schema: "inventory",
                table: "StockScraps",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScrapPieceCount",
                schema: "inventory",
                table: "StockScraps",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferReceipts_PieceCount",
                schema: "inventory",
                table: "TransferReceipts",
                sql: "([DispatchedPieceCount] IS NULL AND [ReceivedPieceCount] IS NULL AND [ShortagePieceCount] IS NULL AND [OveragePieceCount] IS NULL) OR ([DispatchedPieceCount] IS NOT NULL AND [ReceivedPieceCount] IS NOT NULL AND [ShortagePieceCount] IS NOT NULL AND [OveragePieceCount] IS NOT NULL AND [DispatchedPieceCount] > 0 AND [ReceivedPieceCount] > 0 AND [ShortagePieceCount] >= 0 AND [OveragePieceCount] >= 0 AND ([ShortagePieceCount] = 0 OR [OveragePieceCount] = 0) AND [ReceivedPieceCount] + [ShortagePieceCount] - [OveragePieceCount] = [DispatchedPieceCount] AND ([ShortagePieceCount] = 0 OR [ShortageKg] > 0) AND ([OveragePieceCount] = 0 OR [OverageKg] > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferOverageRenewals_PieceCount",
                schema: "inventory",
                table: "TransferOverageRenewals",
                sql: "[OveragePieceCount] IS NULL OR [OveragePieceCount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferBuckets_PieceCount",
                schema: "inventory",
                table: "TransferBuckets",
                sql: "[PieceCount] IS NULL OR [PieceCount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockScraps_PieceCount",
                schema: "inventory",
                table: "StockScraps",
                sql: "([BookPieceCount] IS NULL AND [ScrapPieceCount] IS NULL) OR ([BookPieceCount] IS NOT NULL AND [ScrapPieceCount] IS NOT NULL AND [BookPieceCount] > 0 AND [ScrapPieceCount] >= 0 AND (([ScrapPieceCount] = [BookPieceCount] AND [ScrapKg] = [BookKg]) OR ([ScrapPieceCount] < [BookPieceCount] AND [ScrapKg] < [BookKg])))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[TransferBuckets] WHERE [PieceCount] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferReceipts] WHERE [DispatchedPieceCount] IS NOT NULL
                        OR [ReceivedPieceCount] IS NOT NULL OR [ShortagePieceCount] IS NOT NULL OR [OveragePieceCount] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [inventory].[TransferOverageRenewals] WHERE [OveragePieceCount] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [inventory].[StockScraps] WHERE [BookPieceCount] IS NOT NULL OR [ScrapPieceCount] IS NOT NULL)
                BEGIN
                    THROW 52031, 'Inventory movement piece counts are recorded; AddInventoryMovementPieceCounts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferReceipts_PieceCount",
                schema: "inventory",
                table: "TransferReceipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferOverageRenewals_PieceCount",
                schema: "inventory",
                table: "TransferOverageRenewals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferBuckets_PieceCount",
                schema: "inventory",
                table: "TransferBuckets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockScraps_PieceCount",
                schema: "inventory",
                table: "StockScraps");

            migrationBuilder.DropColumn(
                name: "DispatchedPieceCount",
                schema: "inventory",
                table: "TransferReceipts");

            migrationBuilder.DropColumn(
                name: "OveragePieceCount",
                schema: "inventory",
                table: "TransferReceipts");

            migrationBuilder.DropColumn(
                name: "ReceivedPieceCount",
                schema: "inventory",
                table: "TransferReceipts");

            migrationBuilder.DropColumn(
                name: "ShortagePieceCount",
                schema: "inventory",
                table: "TransferReceipts");

            migrationBuilder.DropColumn(
                name: "OveragePieceCount",
                schema: "inventory",
                table: "TransferOverageRenewals");

            migrationBuilder.DropColumn(
                name: "PieceCount",
                schema: "inventory",
                table: "TransferBuckets");

            migrationBuilder.DropColumn(
                name: "BookPieceCount",
                schema: "inventory",
                table: "StockScraps");

            migrationBuilder.DropColumn(
                name: "ScrapPieceCount",
                schema: "inventory",
                table: "StockScraps");
        }
    }
}
