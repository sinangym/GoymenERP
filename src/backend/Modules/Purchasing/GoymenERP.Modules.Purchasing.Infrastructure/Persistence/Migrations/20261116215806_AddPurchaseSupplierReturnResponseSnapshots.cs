using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseSupplierReturnResponseSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                schema: "purchasing",
                table: "PurchaseSupplierReturnAttempts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseSupplierReturnAttempts_ResultSnapshot",
                schema: "purchasing",
                table: "PurchaseSupplierReturnAttempts",
                sql: "[ResultJson] IS NULL OR ([Outcome] = 1 AND ISJSON([ResultJson]) = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseSupplierReturnAttempts] WHERE [ResultJson] IS NOT NULL)
                    THROW 51953, 'Recorded supplier-return response snapshots require a forward fix.', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseSupplierReturnAttempts_ResultSnapshot",
                schema: "purchasing",
                table: "PurchaseSupplierReturnAttempts");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                schema: "purchasing",
                table: "PurchaseSupplierReturnAttempts");
        }
    }
}
