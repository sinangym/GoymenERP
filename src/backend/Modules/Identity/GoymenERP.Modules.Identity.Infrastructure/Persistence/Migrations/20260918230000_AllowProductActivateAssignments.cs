using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// DEC-PRD-010 (#329): the Product card approval (Activate = 4) becomes an explicit global assignment
    /// operation. Only the operation check widens; no row is written or changed.
    /// Down restores (1, 2, 3) and fails while an Activate assignment exists: forward-fix, never a silent
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
