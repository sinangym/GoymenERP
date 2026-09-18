using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// DEC-PRD-010 (#329): the Product card approval (Activate = 4) becomes an explicit global assignment
    /// operation. Only the operation check widens; no row is written or changed.
    /// Down follows this module's existing rule (AddExplicitProductAssignments): assignment provenance is
    /// retained, so the rollback is refused while any assignment exists; forward-fix, never a silent
    /// deletion of an explicit grant.
    /// </summary>
    public partial class AllowProductActivateAssignments : Migration
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
                sql: "[Operation] IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[ProductMasterAssignments])
                    THROW 51042, 'Product assignment provenance must be retained; use a forward fix.', 1;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductMasterAssignments_Operation",
                schema: "identity",
                table: "ProductMasterAssignments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductMasterAssignments_Operation",
                schema: "identity",
                table: "ProductMasterAssignments",
                sql: "[Operation] IN (1, 2, 3)");
        }
    }
}
