using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds an explicit, nullable label classification. Existing labels remain unknown; the migration
/// never backfills them as Standard and creates no physical stock effect.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20261002110000_AddInventoryLabelClassification")]
public sealed class AddInventoryLabelClassification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Classification",
            schema: "inventory",
            table: "Labels",
            type: "int",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Labels_Classification",
            schema: "inventory",
            table: "Labels",
            sql: "[Classification] IS NULL OR [Classification] IN (1, 2, 3)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM [inventory].[Labels] WHERE [Classification] IS NOT NULL)
                THROW 51055, 'Inventory label classification exists; use a reviewed forward fix.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_Labels_Classification",
            schema: "inventory",
            table: "Labels");

        migrationBuilder.DropColumn(
            name: "Classification",
            schema: "inventory",
            table: "Labels");
    }
}
