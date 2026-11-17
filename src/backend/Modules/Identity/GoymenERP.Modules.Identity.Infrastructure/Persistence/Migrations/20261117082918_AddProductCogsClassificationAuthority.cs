using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCogsClassificationAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductMasterAssignments_Operation",
                schema: "identity",
                table: "ProductMasterAssignments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductMasterAssignments_Operation",
                schema: "identity",
                table: "ProductMasterAssignments",
                sql: "[Operation] IN (1, 2, 3, 4, 5, 6, 7)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[ProductMasterAssignments] WHERE [Operation] = 7)
                BEGIN
                    THROW 51979, 'Explicit COGS classification evidence exists; AddProductCogsClassificationAuthority is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductMasterAssignments_Operation",
                schema: "identity",
                table: "ProductMasterAssignments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductMasterAssignments_Operation",
                schema: "identity",
                table: "ProductMasterAssignments",
                sql: "[Operation] IN (1, 2, 3, 4, 5, 6)");
        }
    }
}
