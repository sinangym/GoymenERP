using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchKilogramAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KilogramAllocationJson",
                schema: "logistics",
                table: "WeighingDifferences",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WeighingDifferences_KilogramAllocation",
                schema: "logistics",
                table: "WeighingDifferences",
                sql: "[KilogramAllocationJson] IS NULL OR (ISJSON([KilogramAllocationJson]) = 1 AND JSON_QUERY(CASE WHEN ISJSON([KilogramAllocationJson]) = 1 THEN [KilogramAllocationJson] ELSE N'{}' END, '$.lines') IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [logistics].[WeighingDifferences] WHERE [KilogramAllocationJson] IS NOT NULL)
                BEGIN
                    THROW 51973, 'Dispatch kilogram source allocations exist; AddDispatchKilogramAllocations is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_WeighingDifferences_KilogramAllocation",
                schema: "logistics",
                table: "WeighingDifferences");

            migrationBuilder.DropColumn(
                name: "KilogramAllocationJson",
                schema: "logistics",
                table: "WeighingDifferences");
        }
    }
}
