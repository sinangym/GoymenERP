using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

/// <summary>
/// Persists the exact Product card version captured by new physical labels. Historical labels stay null: their
/// version is unknown and must never be reconstructed from the current Product card.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20261001160000_AddInventoryLabelProductVersion")]
public sealed class AddInventoryLabelProductVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProductVersion",
            schema: "inventory",
            table: "Labels",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Labels_ProductVersion",
            schema: "inventory",
            table: "Labels",
            sql: "[ProductVersion] IS NULL OR LEN([ProductVersion]) > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM [inventory].[Labels] WHERE [ProductVersion] IS NOT NULL)
                THROW 51054, 'Inventory label ProductVersion provenance exists; use a reviewed forward fix.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_Labels_ProductVersion",
            schema: "inventory",
            table: "Labels");

        migrationBuilder.DropColumn(
            name: "ProductVersion",
            schema: "inventory",
            table: "Labels");
    }
}
