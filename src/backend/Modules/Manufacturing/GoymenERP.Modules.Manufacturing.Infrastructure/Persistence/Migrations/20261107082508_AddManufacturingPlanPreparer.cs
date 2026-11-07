using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingPlanPreparer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "manufacturing",
                table: "Plans",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_CreatedBy",
                schema: "manufacturing",
                table: "Plans",
                sql: "[CreatedBy] IS NULL OR LEN([CreatedBy]) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Üretim U2-d (URT-D2): the preparer decides who may approve or reject a Draft. Dropping it from a Draft that
            // was never revised would let its creator decide the plan after a later Up, so Down refuses (THROW 51190)
            // while such a Draft exists (forward fix). An approved or cancelled plan takes no further decision, and a
            // revised Draft keeps its preparer on its last revision row, so their CreatedBy may go.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[Plans] AS [p] WHERE [p].[CreatedBy] IS NOT NULL
                        AND [p].[Status] = 1
                        AND NOT EXISTS (SELECT 1 FROM [manufacturing].[PlanRevisions] AS [r]
                            WHERE [r].[CompanyId] = [p].[CompanyId] AND [r].[PlanId] = [p].[Id]))
                    THROW 51190, 'An unrevised Manufacturing draft plan records its preparer; forward-fix before downgrade.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_CreatedBy",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "manufacturing",
                table: "Plans");
        }
    }
}
