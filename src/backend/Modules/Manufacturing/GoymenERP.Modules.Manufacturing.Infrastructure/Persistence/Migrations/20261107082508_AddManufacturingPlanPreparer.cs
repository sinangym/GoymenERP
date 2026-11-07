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
            // Üretim U2-d (URT-D2): a recorded preparer is content the previous schema cannot hold.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[Plans] WHERE [CreatedBy] IS NOT NULL)
                    THROW 51190, 'Manufacturing plan preparers exist; forward-fix before downgrade.', 1;
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
