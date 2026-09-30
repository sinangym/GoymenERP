using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// DEC-CAR-003 / PRD-PERM-001 (#99): Product card deactivation (Deactivate = 5) and reactivation
    /// (Reactivate = 6) become explicit global assignment operations, each its own grant. Only the
    /// operation check widens; no row is written or changed and nobody is granted anything.
    /// Down follows this module's existing rule (AddExplicitProductAssignments): assignment provenance is
    /// retained, so the rollback is refused while any assignment exists; forward-fix, never a silent
    /// deletion of an explicit grant.
    /// </summary>
    public partial class AllowProductLifecycleAssignments : Migration
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
                sql: "[Operation] IN (1, 2, 3, 4, 5, 6)");
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
                sql: "[Operation] IN (1, 2, 3, 4)");
        }
    }
}
