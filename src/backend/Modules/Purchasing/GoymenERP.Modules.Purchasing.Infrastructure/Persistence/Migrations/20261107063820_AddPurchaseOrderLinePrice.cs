using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A2a-1 (TEK-01, plan API-20; ALS-D5, ALS-D18, ALS-D51/D60): the purchase order's currency — the Settings money profile
    /// snapshot it was opened under (CurrencyId, CurrencyCode, MoneyProfileId, MoneyProfileVersion, MoneyScale; all five or none,
    /// <c>CK_PurchaseOrders_Money</c>) — and each general line's manual price: UnitPrice and DiscountAmount decimal(24,6),
    /// DiscountPercent decimal(9,6), PriceUnitId; all four null for a line without a price ("fiyat bekliyor"), otherwise a price of zero
    /// or more (an explicit 0 is a price) on a general line with at most one discount in bounds (<c>CK_PurchaseOrderLines_Price</c>). Every column is nullable
    /// and every existing row stays as it was: no order has a currency and no line a price.
    /// <para>
    /// Down refuses (<c>THROW 51169</c>, used by no other migration) while any order has a currency or any line a price: dropping
    /// the columns would silently turn priced orders back into unpriced ones (forward fix). Otherwise it drops the two checks and
    /// the nine columns.
    /// </para>
    /// </summary>
    public partial class AddPurchaseOrderLinePrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MoneyProfileId",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MoneyProfileVersion",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoneyScale",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(24,6)",
                precision: 24,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PriceUnitId",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(24,6)",
                precision: 24,
                scale: 6,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_Money",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "([CurrencyId] IS NULL AND [CurrencyCode] IS NULL AND [MoneyProfileId] IS NULL AND [MoneyProfileVersion] IS NULL AND [MoneyScale] IS NULL) OR ([CurrencyId] IS NOT NULL AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyCode] IS NOT NULL AND LEN([CurrencyCode]) > 0 AND [MoneyProfileId] IS NOT NULL AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileVersion] IS NOT NULL AND [MoneyProfileVersion] >= 1 AND [MoneyScale] IS NOT NULL AND [MoneyScale] >= 0 AND [MoneyScale] <= 28)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrderLines_Price",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                sql: "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL AND [DiscountPercent] IS NULL AND [DiscountAmount] IS NULL) OR ([Kind] = 1 AND [UnitPrice] IS NOT NULL AND [UnitPrice] >= 0 AND [PriceUnitId] IS NOT NULL AND [PriceUnitId] <> '00000000-0000-0000-0000-000000000000' AND ([DiscountPercent] IS NULL OR [DiscountAmount] IS NULL) AND ([DiscountPercent] IS NULL OR ([DiscountPercent] >= 0 AND [DiscountPercent] <= 100)) AND ([DiscountAmount] IS NULL OR ([DiscountAmount] >= 0 AND [DiscountAmount] <= [UnitPrice])))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrders] WHERE [CurrencyId] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrderLines] WHERE [UnitPrice] IS NOT NULL)
                BEGIN
                    THROW 51169, 'Priced purchase orders exist; AddPurchaseOrderLinePrice is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_Money",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrderLines_Price",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MoneyProfileId",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MoneyProfileVersion",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MoneyScale",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "PriceUnitId",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                schema: "purchasing",
                table: "PurchaseOrderLines");
        }
    }
}
