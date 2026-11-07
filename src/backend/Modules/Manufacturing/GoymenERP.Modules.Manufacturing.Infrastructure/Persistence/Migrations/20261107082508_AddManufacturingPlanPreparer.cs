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
            // Üretim U2-d (URT-D2): the preparer is a control datum, not plan content: the creating actor is also on the
            // plan's audit event (manufacturing.plan.created.v1) and each reviser on its revision row. Down drops it so a
            // rollback stays possible while plans exist. After a later Up, a plan never revised has no preparer and is
            // decided by anyone holding the approval (the pre-U2-d behaviour); a revised plan keeps its last reviser.
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
