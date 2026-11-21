using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseSupplierReturnCostAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CostAllocationJson",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseSupplierReturns_CostAllocationJson",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                sql: "[CostAllocationJson] IS NULL OR ISJSON([CostAllocationJson]) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Source attribution and the invoice labels reviewed by the approver are durable history.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseSupplierReturns] WHERE [CostAllocationJson] IS NOT NULL)
                BEGIN
                    THROW 52012, 'Supplier return source allocation history exists; use a forward fix.', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseSupplierReturns_CostAllocationJson",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.DropColumn(
                name: "CostAllocationJson",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");
        }
    }
}
