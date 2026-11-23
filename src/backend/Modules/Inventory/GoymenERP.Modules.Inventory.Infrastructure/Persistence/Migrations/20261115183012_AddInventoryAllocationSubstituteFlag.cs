using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAllocationSubstituteFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSubstitute",
                schema: "inventory",
                table: "Allocations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Substitute",
                schema: "inventory",
                table: "Allocations",
                sql: "[IsSubstitute] = 0 OR ([DemandKind] IS NOT NULL AND [DemandKind] = 'sales.order-line')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix only: dropping the column would silently turn a confirmed substitute ("muadil") allocation
            // into an ordinary one, so Down refuses while any substitute allocation exists.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[Allocations] WHERE [IsSubstitute] = 1)
                BEGIN
                    THROW 51653, 'A substitute allocation exists; AddInventoryAllocationSubstituteFlag is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Substitute",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "IsSubstitute",
                schema: "inventory",
                table: "Allocations");
        }
    }
}
