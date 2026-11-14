using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLabelProductionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductionStatus",
                schema: "inventory",
                table: "Labels",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Labels_ProductionStatus",
                schema: "inventory",
                table: "Labels",
                sql: "[ProductionStatus] IS NULL OR [ProductionStatus] IN (1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // STK-S9 (BS-5): forward fix. A label's production status is a recorded production fact; dropping the
            // column would silently erase it, so Down refuses while any label carries one.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[Labels] WHERE [ProductionStatus] IS NOT NULL)
                BEGIN
                    THROW 51291, 'A label carries a production status; AddInventoryLabelProductionStatus is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Labels_ProductionStatus",
                schema: "inventory",
                table: "Labels");

            migrationBuilder.DropColumn(
                name: "ProductionStatus",
                schema: "inventory",
                table: "Labels");
        }
    }
}
