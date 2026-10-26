using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ManufacturingDbContext))]
[Migration("20261014120000_AddManufacturingPlanCutLines")]
public sealed class AddManufacturingPlanCutLines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        CreatePlanCutLines(migrationBuilder);
        CreatePlanRevisionCutLines(migrationBuilder);
    }

    private static void CreatePlanCutLines(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlanCutLines",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Ordinal = table.Column<int>(type: "int", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                LengthMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Count = table.Column<int>(type: "int", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlanCutLines", x => new { x.CompanyId, x.PlanId, x.LineId });
                table.CheckConstraint("CK_PlanCutLines_Identity",
                    "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [Ordinal] >= 1");
                table.CheckConstraint("CK_PlanCutLines_Length", "[LengthMm] > 0");
                table.CheckConstraint("CK_PlanCutLines_Count", "[Count] >= 1");
                table.ForeignKey(
                    name: "FK_PlanCutLines_Plans_CompanyId_PlanId",
                    columns: x => new { x.CompanyId, x.PlanId },
                    principalSchema: "manufacturing",
                    principalTable: "Plans",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_PlanCutLines_CompanyId_PlanId_Ordinal",
            schema: "manufacturing",
            table: "PlanCutLines",
            columns: new[] { "CompanyId", "PlanId", "Ordinal" },
            unique: true);
    }

    private static void CreatePlanRevisionCutLines(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlanRevisionCutLines",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RevisionNo = table.Column<int>(type: "int", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Ordinal = table.Column<int>(type: "int", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                LengthMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Count = table.Column<int>(type: "int", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlanRevisionCutLines",
                    x => new { x.CompanyId, x.PlanId, x.RevisionNo, x.LineId });
                table.CheckConstraint("CK_PlanRevisionCutLines_Identity",
                    "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [Ordinal] >= 1");
                table.CheckConstraint("CK_PlanRevisionCutLines_Length", "[LengthMm] > 0");
                table.CheckConstraint("CK_PlanRevisionCutLines_Count", "[Count] >= 1");
                table.ForeignKey(
                    name: "FK_PlanRevisionCutLines_PlanRevisions_CompanyId_PlanId_RevisionNo",
                    columns: x => new { x.CompanyId, x.PlanId, x.RevisionNo },
                    principalSchema: "manufacturing",
                    principalTable: "PlanRevisions",
                    principalColumns: new[] { "CompanyId", "PlanId", "RevisionNo" },
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_PlanRevisionCutLines_CompanyId_PlanId_RevisionNo_Ordinal",
            schema: "manufacturing",
            table: "PlanRevisionCutLines",
            columns: new[] { "CompanyId", "PlanId", "RevisionNo", "Ordinal" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [manufacturing].[PlanCutLines])
                OR EXISTS (SELECT 1 FROM [manufacturing].[PlanRevisionCutLines])
                THROW 51047, 'Manufacturing cut line history must be forward-fixed before downgrade.', 1;
            """);
        migrationBuilder.DropTable(name: "PlanRevisionCutLines", schema: "manufacturing");
        migrationBuilder.DropTable(name: "PlanCutLines", schema: "manufacturing");
    }
}
