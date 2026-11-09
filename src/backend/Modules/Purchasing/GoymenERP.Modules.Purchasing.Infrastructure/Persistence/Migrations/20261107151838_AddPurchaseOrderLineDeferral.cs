using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A2a-2 (ALS-D38, ALS-D75 "Satış'la aynı"): each general line's maturity surcharge (vade farkı) — DeferralPercent decimal(9,6), a
    /// percent of the discounted unit price, or DeferralAmount decimal(24,6), an amount per unit — checked by
    /// <c>CK_PurchaseOrderLines_Deferral</c>: none, or exactly one in bounds beside a unit price above 0 (no surcharge on a 0 price).
    /// <c>CK_PurchaseOrderLines_Price</c> is unchanged. Both columns are nullable and every existing line stays as it was (no surcharge;
    /// a deferred price typed directly is a plain price, ALS-D76); no backfill.
    /// <para>
    /// Down refuses (<c>THROW 51171</c>, used by no other migration on main or in an open PR) while any line carries a surcharge:
    /// dropping the columns would silently lower those lines' net price and amount (forward fix). Otherwise it drops the check and the
    /// two columns.
    /// </para>
    /// </summary>
    public partial class AddPurchaseOrderLineDeferral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DeferralAmount",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(24,6)",
                precision: 24,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeferralPercent",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_Deferral",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "([DeferralPercent] IS NULL AND [DeferralAmount] IS NULL) OR ([UnitPrice] IS NOT NULL AND [UnitPrice] > 0 AND ([DeferralPercent] IS NULL OR [DeferralAmount] IS NULL) AND ([DeferralPercent] IS NULL OR ([DeferralPercent] >= 0 AND [DeferralPercent] <= 100)) AND ([DeferralAmount] IS NULL OR [DeferralAmount] >= 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderLines] WHERE [DeferralPercent] IS NOT NULL OR [DeferralAmount] IS NOT NULL)
                BEGIN
                    THROW 51171, 'Purchase order lines with a maturity surcharge exist; AddPurchaseOrderLineDeferral is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_Deferral",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "DeferralAmount",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "DeferralPercent",
                schema: "purchasing",
                table: "PurchaseOrderLines");
        }
    }
}
