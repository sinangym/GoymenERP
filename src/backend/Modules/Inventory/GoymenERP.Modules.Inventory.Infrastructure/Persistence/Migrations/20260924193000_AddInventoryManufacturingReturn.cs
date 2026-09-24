using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the append-only production input-return movement kind. Down refuses while a return exists;
/// removing a durable physical fact would make the historical ledger invalid, so rollback requires
/// a reviewed forward fix.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260924193000_AddInventoryManufacturingReturn")]
public sealed class AddInventoryManufacturingReturn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Movements_Kind",
            schema: "inventory",
            table: "Movements");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Movements_Kind",
            schema: "inventory",
            table: "Movements",
            sql: "[Kind] BETWEEN 1 AND 5");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM [inventory].[Movements] WHERE [Kind] = 5)
                THROW 51053, 'Manufacturing return movements exist; use a reviewed forward fix.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_Movements_Kind",
            schema: "inventory",
            table: "Movements");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Movements_Kind",
            schema: "inventory",
            table: "Movements",
            sql: "[Kind] BETWEEN 1 AND 4");
    }
}
