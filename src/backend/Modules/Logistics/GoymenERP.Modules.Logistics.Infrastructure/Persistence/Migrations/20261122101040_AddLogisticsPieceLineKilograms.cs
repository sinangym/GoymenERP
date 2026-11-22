using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLogisticsPieceLineKilograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QuantityKg",
                schema: "logistics",
                table: "LoadingShares",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityKg",
                schema: "logistics",
                table: "DispatchNoteLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LoadingShares_QuantityKg",
                schema: "logistics",
                table: "LoadingShares",
                sql: "[QuantityKg] IS NULL OR ([QuantityKg] > 0 AND [Kind] IN (1, 2))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DispatchNoteLines_QuantityKg",
                schema: "logistics",
                table: "DispatchNoteLines",
                sql: "[QuantityKg] IS NULL OR [QuantityKg] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // AK-4 (KLN-D119): a piece line's physical kg is the only record of what left with its pieces; while any exists the
            // migration is not rolled back (forward fix). An empty store (or kg-only shares) rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [logistics].[LoadingShares] WHERE [QuantityKg] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [logistics].[DispatchNoteLines] WHERE [QuantityKg] IS NOT NULL)
                BEGIN
                    THROW 52052, 'Piece-line physical kilograms exist; AddLogisticsPieceLineKilograms is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_LoadingShares_QuantityKg",
                schema: "logistics",
                table: "LoadingShares");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DispatchNoteLines_QuantityKg",
                schema: "logistics",
                table: "DispatchNoteLines");

            migrationBuilder.DropColumn(
                name: "QuantityKg",
                schema: "logistics",
                table: "LoadingShares");

            migrationBuilder.DropColumn(
                name: "QuantityKg",
                schema: "logistics",
                table: "DispatchNoteLines");
        }
    }
}
