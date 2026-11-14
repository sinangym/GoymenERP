using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingPlanDueDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DueDateRevision",
                schema: "manufacturing",
                table: "Plans",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestedDueDate",
                schema: "manufacturing",
                table: "Plans",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_DueDateRevision",
                schema: "manufacturing",
                table: "Plans",
                sql: "[DueDateRevision] >= 0 AND ([RequestedDueDate] IS NULL OR [DueDateRevision] >= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Üretim F-010a (URT-D29): a plan whose termin revision moved holds, or held, a hand-entered termin, and its audit
            // events count that revision. The earlier schema cannot say a termin was entered, so Down refuses (THROW 51281)
            // while such a plan exists (forward fix). A store where no termin was ever entered rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[Plans] WHERE [DueDateRevision] > 0 OR [RequestedDueDate] IS NOT NULL)
                    THROW 51281, 'A Manufacturing plan holds or held a hand-entered termin; AddManufacturingPlanDueDate is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_DueDateRevision",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "DueDateRevision",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "RequestedDueDate",
                schema: "manufacturing",
                table: "Plans");
        }
    }
}
