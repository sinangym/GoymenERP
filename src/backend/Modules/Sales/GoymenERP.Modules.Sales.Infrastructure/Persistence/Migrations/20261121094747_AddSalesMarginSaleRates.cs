using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMarginSaleRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SaleExchangeRate",
                schema: "sales",
                table: "DraftMarginCosts",
                type: "decimal(16,8)",
                precision: 16,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleRateCurrencyId",
                schema: "sales",
                table: "DraftMarginCosts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SaleRateDay",
                schema: "sales",
                table: "DraftMarginCosts",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DraftMarginCosts_SaleRate",
                schema: "sales",
                table: "DraftMarginCosts",
                sql: "([SaleRateCurrencyId] IS NULL AND [SaleExchangeRate] IS NULL AND [SaleRateDay] IS NULL) OR ([SaleRateCurrencyId] IS NOT NULL AND [SaleRateCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [SaleExchangeRate] IS NOT NULL AND [SaleExchangeRate] > 0 AND [SaleRateDay] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SAT-D426: a frozen approval sale rate is the margin's evidence; it is never dropped silently.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[DraftMarginCosts] WHERE [SaleRateCurrencyId] IS NOT NULL)
                BEGIN
                    THROW 52013, 'Recorded approval sale rates exist; AddSalesMarginSaleRates is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_DraftMarginCosts_SaleRate",
                schema: "sales",
                table: "DraftMarginCosts");

            migrationBuilder.DropColumn(
                name: "SaleExchangeRate",
                schema: "sales",
                table: "DraftMarginCosts");

            migrationBuilder.DropColumn(
                name: "SaleRateCurrencyId",
                schema: "sales",
                table: "DraftMarginCosts");

            migrationBuilder.DropColumn(
                name: "SaleRateDay",
                schema: "sales",
                table: "DraftMarginCosts");
        }
    }
}
