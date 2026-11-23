using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// KLN-D119 (AK-2): nullable piece counts on stock balances, movement lines, split/merge sources/outputs and stock counts.
    /// Pure DDL; existing rows stay null (no backfill). Down is a forward fix: it refuses (THROW 52021) while any row carries
    /// a piece count, because dropping the columns would silently lose the piece facts; an empty or piece-less store rolls
    /// back freely.
    /// </summary>
    public partial class AddInventoryLabelPieceCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookPieceCount",
                schema: "inventory",
                table: "StockCounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CountedPieceCount",
                schema: "inventory",
                table: "StockCounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PieceCount",
                schema: "inventory",
                table: "MovementLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PieceCount",
                schema: "inventory",
                table: "LabelTransformationOutputs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PieceCount",
                schema: "inventory",
                table: "LabelTransformationInputs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PieceCount",
                schema: "inventory",
                table: "Balances",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockCounts_PieceCount",
                schema: "inventory",
                table: "StockCounts",
                sql: "([BookPieceCount] IS NULL AND [CountedPieceCount] IS NULL) OR ([BookPieceCount] IS NOT NULL AND [CountedPieceCount] IS NOT NULL AND (([BookPieceCount] = 0 AND [BookKg] = 0) OR ([BookPieceCount] > 0 AND [BookKg] > 0)) AND (([CountedPieceCount] = 0 AND [CountedKg] = 0) OR ([CountedPieceCount] > 0 AND [CountedKg] > 0)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementLines_PieceCount",
                schema: "inventory",
                table: "MovementLines",
                sql: "[PieceCount] IS NULL OR [PieceCount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LabelTransformationOutputs_PieceCount",
                schema: "inventory",
                table: "LabelTransformationOutputs",
                sql: "[PieceCount] IS NULL OR [PieceCount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LabelTransformationInputs_PieceCount",
                schema: "inventory",
                table: "LabelTransformationInputs",
                sql: "[PieceCount] IS NULL OR [PieceCount] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Balances_PieceCount",
                schema: "inventory",
                table: "Balances",
                sql: "[PieceCount] IS NULL OR ([PieceCount] = 0 AND [QuantityKg] = 0) OR ([PieceCount] > 0 AND [QuantityKg] > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[Balances] WHERE [PieceCount] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [inventory].[MovementLines] WHERE [PieceCount] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [inventory].[LabelTransformationInputs] WHERE [PieceCount] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [inventory].[LabelTransformationOutputs] WHERE [PieceCount] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [inventory].[StockCounts] WHERE [BookPieceCount] IS NOT NULL OR [CountedPieceCount] IS NOT NULL)
                BEGIN
                    THROW 52021, 'Inventory piece counts are recorded; AddInventoryLabelPieceCounts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockCounts_PieceCount",
                schema: "inventory",
                table: "StockCounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementLines_PieceCount",
                schema: "inventory",
                table: "MovementLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LabelTransformationOutputs_PieceCount",
                schema: "inventory",
                table: "LabelTransformationOutputs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LabelTransformationInputs_PieceCount",
                schema: "inventory",
                table: "LabelTransformationInputs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Balances_PieceCount",
                schema: "inventory",
                table: "Balances");

            migrationBuilder.DropColumn(
                name: "BookPieceCount",
                schema: "inventory",
                table: "StockCounts");

            migrationBuilder.DropColumn(
                name: "CountedPieceCount",
                schema: "inventory",
                table: "StockCounts");

            migrationBuilder.DropColumn(
                name: "PieceCount",
                schema: "inventory",
                table: "MovementLines");

            migrationBuilder.DropColumn(
                name: "PieceCount",
                schema: "inventory",
                table: "LabelTransformationOutputs");

            migrationBuilder.DropColumn(
                name: "PieceCount",
                schema: "inventory",
                table: "LabelTransformationInputs");

            migrationBuilder.DropColumn(
                name: "PieceCount",
                schema: "inventory",
                table: "Balances");
        }
    }
}
