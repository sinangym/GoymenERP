using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowPurchaseSupplierReturnReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseSupplierReturns_Values",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseSupplierReturns_Values",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                sql: "[QuantityKg] > 0 AND [UnitCostTryPerKg] > 0 AND [SourceBlock] IN (N'Free', N'Blocked') AND ([ReplacementExpected] = 0 OR ([ExpectedPurchaseOrderVersion] IS NOT NULL AND [UndoQuantity] IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ALS-D91 (K3-B P1): forward fix — a recorded "replacement expected" supplier return would violate the earlier
            // CHECK and lose the reopened order quantity's reason. The rollback refuses while one exists; a store without one
            // rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseSupplierReturns] WHERE [ReplacementExpected] = 1)
                BEGIN
                    THROW 52300, 'Purchase supplier returns with an expected replacement exist; AllowPurchaseSupplierReturnReplacement is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseSupplierReturns_Values",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseSupplierReturns_Values",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                sql: "[QuantityKg] > 0 AND [UnitCostTryPerKg] > 0 AND [SourceBlock] IN (N'Free', N'Blocked') AND [ReplacementExpected] = 0");
        }
    }
}
