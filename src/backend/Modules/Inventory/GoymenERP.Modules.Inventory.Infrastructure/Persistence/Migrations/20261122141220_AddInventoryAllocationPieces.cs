using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// KLN-D119 / SAT-D434 (AK-4b): nullable reserved and remaining piece counts on stock allocations. Pure DDL; existing
    /// rows stay null (no backfill). Down is a forward fix: it refuses (THROW 52022) while any allocation carries pieces,
    /// because dropping the columns would silently lose the reserved piece facts; a store without allocation pieces rolls
    /// back freely.
    /// </summary>
    public partial class AddInventoryAllocationPieces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AllocatedPieces",
                schema: "inventory",
                table: "Allocations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemainingPieces",
                schema: "inventory",
                table: "Allocations",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Pieces",
                schema: "inventory",
                table: "Allocations",
                sql: "([AllocatedPieces] IS NULL AND [RemainingPieces] IS NULL) OR ([AllocatedPieces] IS NOT NULL AND [RemainingPieces] IS NOT NULL AND [AllocatedPieces] > 0 AND [RemainingPieces] >= 0 AND [RemainingPieces] <= [AllocatedPieces])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[Allocations] WHERE [AllocatedPieces] IS NOT NULL OR [RemainingPieces] IS NOT NULL)
                BEGIN
                    THROW 52022, 'Inventory allocation pieces are recorded; AddInventoryAllocationPieces is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Pieces",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "AllocatedPieces",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "RemainingPieces",
                schema: "inventory",
                table: "Allocations");
        }
    }
}
