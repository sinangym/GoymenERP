using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A2a-1c (ALS-D72, TEK-01): the purchase order's money snapshot gains UnitPriceScale (int, nullable) — the Settings money
    /// profile's unit-price scale for <c>purchasing.order.v1</c> (4 for TRY) — checked by <c>CK_PurchaseOrders_UnitPriceScale</c>:
    /// null, or present only beside a currency and within 0..28. <c>CK_PurchaseOrders_Money</c> and <c>CK_PurchaseOrderLines_Price</c>
    /// are unchanged. Every existing row stays as it was (null): an order recorded before A2a-1c keeps the amount scale for its unit
    /// prices, exactly as A2a-1 wrote them; no backfill.
    /// <para>
    /// Down refuses (<c>THROW 51170</c>, used by no other migration) while any order carries a unit-price scale: dropping the column
    /// would leave unit prices wider than the amount scale that the domain then refuses to rebuild (forward fix). Otherwise it drops
    /// the check and the column.
    /// </para>
    /// </summary>
    public partial class AddPurchaseOrderUnitPriceScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnitPriceScale",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_UnitPriceScale",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "[UnitPriceScale] IS NULL OR ([CurrencyId] IS NOT NULL AND [UnitPriceScale] >= 0 AND [UnitPriceScale] <= 28)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrders] WHERE [UnitPriceScale] IS NOT NULL)
                BEGIN
                    THROW 51170, 'Purchase orders with a unit price scale exist; AddPurchaseOrderUnitPriceScale is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_UnitPriceScale",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "UnitPriceScale",
                schema: "purchasing",
                table: "PurchaseOrders");
        }
    }
}
