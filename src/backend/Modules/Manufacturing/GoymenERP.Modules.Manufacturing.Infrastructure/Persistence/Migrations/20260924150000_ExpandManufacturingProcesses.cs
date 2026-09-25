using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// DEC-PRDCT-001: expands the durable plan/order process discriminator from Slitting alone to the
/// five locked production processes. No existing row is rewritten. Down remains safe only while
/// every stored plan and order is Slitting; otherwise it refuses before changing either constraint.
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration("20260924150000_ExpandManufacturingProcesses")]
public sealed class ExpandManufacturingProcesses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Plans_Enums",
            schema: "manufacturing",
            table: "Plans");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Plans_Enums",
            schema: "manufacturing",
            table: "Plans",
            sql: "[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2)");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Orders_Enums",
            schema: "manufacturing",
            table: "Orders");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Orders_Enums",
            schema: "manufacturing",
            table: "Orders",
            sql: "[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [manufacturing].[Plans] WHERE [Process] <> 1)
                OR EXISTS (SELECT 1 FROM [manufacturing].[Orders] WHERE [Process] <> 1)
                THROW 51047, 'Manufacturing process persistence requires a forward fix.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_Plans_Enums",
            schema: "manufacturing",
            table: "Plans");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Plans_Enums",
            schema: "manufacturing",
            table: "Plans",
            sql: "[Process] IN (1) AND [Status] IN (1, 2)");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Orders_Enums",
            schema: "manufacturing",
            table: "Orders");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Orders_Enums",
            schema: "manufacturing",
            table: "Orders",
            sql: "[Process] IN (1) AND [Status] IN (1, 2)");
    }
}
