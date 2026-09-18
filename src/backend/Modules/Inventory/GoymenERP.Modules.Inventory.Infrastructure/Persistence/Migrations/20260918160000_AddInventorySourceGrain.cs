using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

/// <summary>
/// INV-3 (#293 ORG P3-1): a source is answered whole or line by line per movement kind, never both.
/// One grain row per (company, kind, source); every movement references it with its own grain.
/// The new movement column has no default: Inventory is not wired into the host yet, so no
/// deployed table holds movements, and a table that did would refuse this migration rather than
/// receive a guessed grain.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260918160000_AddInventorySourceGrain")]
public sealed class AddInventorySourceGrain : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SourceGrains",
            schema: "inventory",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                SourceKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Grain = table.Column<int>(type: "int", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SourceGrains", x => new { x.CompanyId, x.Kind, x.SourceKind, x.SourceId });
                table.UniqueConstraint("AK_SourceGrains_CompanyId_Kind_SourceKind_SourceId_Grain",
                    x => new { x.CompanyId, x.Kind, x.SourceKind, x.SourceId, x.Grain });
                table.CheckConstraint("CK_SourceGrains_Grain", "[Grain] IN (1, 2)");
                table.CheckConstraint("CK_SourceGrains_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND LEN([SourceKind]) > 0");
                table.ForeignKey(
                    name: "FK_SourceGrains_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddColumn<int>(
            name: "SourceGrain",
            schema: "inventory",
            table: "Movements",
            type: "int",
            nullable: false);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Movements_SourceGrain",
            schema: "inventory",
            table: "Movements",
            sql: "([SourceLineId] IS NULL AND [SourceGrain] = 1) OR ([SourceLineId] IS NOT NULL AND [SourceGrain] = 2)");

        migrationBuilder.CreateIndex(
            name: "IX_Movements_CompanyId_Kind_SourceKind_SourceId_SourceGrain",
            schema: "inventory",
            table: "Movements",
            columns: new[] { "CompanyId", "Kind", "SourceKind", "SourceId", "SourceGrain" });

        migrationBuilder.AddForeignKey(
            name: "FK_Movements_SourceGrains_CompanyId_Kind_SourceKind_SourceId_SourceGrain",
            schema: "inventory",
            table: "Movements",
            columns: new[] { "CompanyId", "Kind", "SourceKind", "SourceId", "SourceGrain" },
            principalSchema: "inventory",
            principalTable: "SourceGrains",
            principalColumns: new[] { "CompanyId", "Kind", "SourceKind", "SourceId", "Grain" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Movements_SourceGrains_CompanyId_Kind_SourceKind_SourceId_SourceGrain",
            schema: "inventory",
            table: "Movements");

        migrationBuilder.DropIndex(
            name: "IX_Movements_CompanyId_Kind_SourceKind_SourceId_SourceGrain",
            schema: "inventory",
            table: "Movements");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Movements_SourceGrain",
            schema: "inventory",
            table: "Movements");

        migrationBuilder.DropColumn(name: "SourceGrain", schema: "inventory", table: "Movements");

        migrationBuilder.DropTable(name: "SourceGrains", schema: "inventory");
    }
}
