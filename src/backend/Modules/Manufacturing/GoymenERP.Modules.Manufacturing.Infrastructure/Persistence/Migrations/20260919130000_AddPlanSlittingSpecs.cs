using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// MFG-6 slice 2: a plan's slitting spec as plan content. The slit lines (band width decimal(18,3) mm,
/// count) in their own table, bound by (company, plan) like the plan's inputs, and the spec's optional
/// winding target as a nullable plan column. No row is written: existing plans carry no spec and keep
/// their content and hash. Down refuses (THROW 51047, Manufacturing's one module number) while any spec
/// exists: a stored spec is plan content and is never dropped — forward fix.
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration("20260919130000_AddPlanSlittingSpecs")]
public sealed class AddPlanSlittingSpecs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "WindingTargetKg",
            schema: "manufacturing",
            table: "Plans",
            type: "decimal(18,3)",
            precision: 18,
            scale: 3,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Plans_WindingTarget",
            schema: "manufacturing",
            table: "Plans",
            sql: "[WindingTargetKg] IS NULL OR [WindingTargetKg] > 0");

        migrationBuilder.CreateTable(
            name: "PlanSlitLines",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                BandWidthMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                BandCount = table.Column<int>(type: "int", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlanSlitLines", x => new { x.CompanyId, x.PlanId, x.Sequence });
                table.CheckConstraint("CK_PlanSlitLines_Count", "[BandCount] >= 1");
                table.CheckConstraint("CK_PlanSlitLines_Sequence", "[Sequence] >= 1");
                table.CheckConstraint("CK_PlanSlitLines_Width", "[BandWidthMm] > 0");
                table.ForeignKey(
                    name: "FK_PlanSlitLines_Plans_CompanyId_PlanId",
                    columns: x => new { x.CompanyId, x.PlanId },
                    principalSchema: "manufacturing",
                    principalTable: "Plans",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [manufacturing].[PlanSlitLines])
                OR EXISTS (SELECT 1 FROM [manufacturing].[Plans] WHERE [WindingTargetKg] IS NOT NULL)
                THROW 51047, 'Manufacturing plan slitting specs require a forward fix.', 1;
            """);

        migrationBuilder.DropTable(name: "PlanSlitLines", schema: "manufacturing");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Plans_WindingTarget",
            schema: "manufacturing",
            table: "Plans");

        migrationBuilder.DropColumn(
            name: "WindingTargetKg",
            schema: "manufacturing",
            table: "Plans");
    }
}
