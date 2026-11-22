using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasePieceKilogramFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TheoreticalKilogramsPerUnit",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TheoreticalKilogramsSourceVersion",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MeasuredQuantityKg",
                schema: "purchasing",
                table: "PurchaseAcceptanceShares",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_TheoreticalKilograms",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "([TheoreticalKilogramsPerUnit] IS NULL AND [TheoreticalKilogramsSourceVersion] IS NULL) OR ([TheoreticalKilogramsPerUnit] IS NOT NULL AND [TheoreticalKilogramsPerUnit] > 0 AND [TheoreticalKilogramsSourceVersion] IS NOT NULL AND LEN([TheoreticalKilogramsSourceVersion]) > 0 AND [UnitIsKilogram] = 0 AND [Kind] = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseAcceptanceShares_MeasuredKg",
                schema: "purchasing",
                table: "PurchaseAcceptanceShares",
                sql: "[MeasuredQuantityKg] IS NULL OR [MeasuredQuantityKg] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // AK-3 (SAT-D437, ALS-D119): forward fix — a recorded theoretical kg/piece snapshot or a weighed piece-line acceptance
            // would lose its meaning; the rollback refuses while one exists and an empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderLines] WHERE [TheoreticalKilogramsPerUnit] IS NOT NULL OR [TheoreticalKilogramsSourceVersion] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseAcceptanceShares] WHERE [MeasuredQuantityKg] IS NOT NULL)
                BEGIN
                    THROW 52041, 'Purchase piece-line kilogram facts exist; AddPurchasePieceKilogramFacts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_TheoreticalKilograms",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseAcceptanceShares_MeasuredKg",
                schema: "purchasing",
                table: "PurchaseAcceptanceShares");

            migrationBuilder.DropColumn(
                name: "TheoreticalKilogramsPerUnit",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "TheoreticalKilogramsSourceVersion",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "MeasuredQuantityKg",
                schema: "purchasing",
                table: "PurchaseAcceptanceShares");
        }
    }
}
