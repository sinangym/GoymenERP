using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// MFG-PLAN-EDIT: the Draft revisions of a plan, APPEND-ONLY beside the plan's own rows (which stay the plan as
/// created, revision 0): <c>PlanRevisions</c> (one row per recorded edit: its work center, target, reason, who, when
/// and the plan version it produced) and <c>PlanRevisionInputs</c> (its complete planned inputs with the accepted
/// Inventory label version). Down refuses with Manufacturing's module number 51047 while any revision is stored:
/// forward fix, no history is dropped. Empty schemas roll back cleanly.
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration("20260927180000_AddManufacturingPlanRevisions")]
public sealed class AddManufacturingPlanRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlanRevisions",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RevisionNo = table.Column<int>(type: "int", nullable: false),
                WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetBasis = table.Column<int>(type: "int", nullable: true),
                TargetKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                PlanVersion = table.Column<long>(type: "bigint", nullable: false),
                RevisedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RevisedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlanRevisions", x => new { x.CompanyId, x.PlanId, x.RevisionNo });
                table.CheckConstraint("CK_PlanRevisions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PlanId] <> '00000000-0000-0000-0000-000000000000' AND [WorkCenterId] <> '00000000-0000-0000-0000-000000000000' AND [RevisionNo] >= 1 AND [PlanVersion] >= 2");
                table.CheckConstraint("CK_PlanRevisions_Target", "([TargetBasis] IS NULL AND [TargetKg] IS NULL) OR ([TargetBasis] IN (1, 2) AND [TargetKg] > 0)");
                table.CheckConstraint("CK_PlanRevisions_Text", "LEN([Reason]) > 0 AND LEN([RevisedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_PlanRevisions_Plans_CompanyId_PlanId",
                    columns: x => new { x.CompanyId, x.PlanId },
                    principalSchema: "manufacturing",
                    principalTable: "Plans",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PlanRevisions_WorkCenters_CompanyId_WorkCenterId",
                    columns: x => new { x.CompanyId, x.WorkCenterId },
                    principalSchema: "organization",
                    principalTable: "WorkCenters",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PlanRevisionInputs",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RevisionNo = table.Column<int>(type: "int", nullable: false),
                ParentRollId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                PlannedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                ExpectedLabelVersion = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlanRevisionInputs", x => new { x.CompanyId, x.PlanId, x.RevisionNo, x.ParentRollId });
                table.CheckConstraint("CK_PlanRevisionInputs_LabelVersion", "[ExpectedLabelVersion] >= 1");
                table.CheckConstraint("CK_PlanRevisionInputs_Mass", "[PlannedKg] > 0");
                table.CheckConstraint("CK_PlanRevisionInputs_Roll", "[ParentRollId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PlanRevisionInputs_Sequence", "[Sequence] >= 1");
                table.ForeignKey(
                    name: "FK_PlanRevisionInputs_PlanRevisions_CompanyId_PlanId_RevisionNo",
                    columns: x => new { x.CompanyId, x.PlanId, x.RevisionNo },
                    principalSchema: "manufacturing",
                    principalTable: "PlanRevisions",
                    principalColumns: new[] { "CompanyId", "PlanId", "RevisionNo" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlanRevisionInputs_CompanyId_PlanId_RevisionNo_Sequence",
            schema: "manufacturing",
            table: "PlanRevisionInputs",
            columns: new[] { "CompanyId", "PlanId", "RevisionNo", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlanRevisions_CompanyId_WorkCenterId",
            schema: "manufacturing",
            table: "PlanRevisions",
            columns: new[] { "CompanyId", "WorkCenterId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [manufacturing].[PlanRevisions])
                OR EXISTS (SELECT 1 FROM [manufacturing].[PlanRevisionInputs])
                THROW 51047, 'Manufacturing plan revisions must be forward-fixed before downgrade.', 1;
            """);

        migrationBuilder.DropTable(
            name: "PlanRevisionInputs",
            schema: "manufacturing");

        migrationBuilder.DropTable(
            name: "PlanRevisions",
            schema: "manufacturing");
    }
}
