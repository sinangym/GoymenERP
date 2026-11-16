using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowInventoryZeroBookStockCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockCounts_State",
                schema: "inventory",
                table: "StockCounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockCounts_State",
                schema: "inventory",
                table: "StockCounts",
                sql: "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2) AND [LabelVersion] >= 1 AND [BookKg] >= 0 AND [CountedKg] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[StockCounts] WHERE [BookKg] = 0)
                BEGIN
                    THROW 51748, 'Inventory stock counts of a label shown at zero exist; AllowInventoryZeroBookStockCounts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockCounts_State",
                schema: "inventory",
                table: "StockCounts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockCounts_State",
                schema: "inventory",
                table: "StockCounts",
                sql: "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2) AND [LabelVersion] >= 1 AND [BookKg] > 0 AND [CountedKg] >= 0");
        }
    }
}
