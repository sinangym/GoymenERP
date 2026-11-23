using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenPostingMappingsToCogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PostingAccountMappingRows_Key",
                schema: "accounting",
                table: "PostingAccountMappingRows");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PostingAccountMappingRows_Key",
                schema: "accounting",
                table: "PostingAccountMappingRows",
                sql: "([Purpose] = N'sales-invoice' AND [MappingKey] IN (N'receivable', N'revenue', N'vat-output', N'withholding-receivable')) OR ([Purpose] = N'purchase-invoice' AND [MappingKey] IN (N'payable', N'inventory', N'vat-input', N'withholding-payable', N'expense')) OR ([Purpose] = N'cost-of-goods-sold' AND [MappingKey] IN (N'trade-goods', N'manufactured') AND [ItemType] IS NULL AND [LinePurpose] IS NULL AND [IsExport] IS NULL AND [VatRateCode] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [accounting].[PostingAccountMappingRows] WHERE [Purpose] = N'cost-of-goods-sold')
                BEGIN
                    ;THROW 51929, N'COGS mapping choices exist; WidenPostingMappingsToCogs is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PostingAccountMappingRows_Key",
                schema: "accounting",
                table: "PostingAccountMappingRows");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PostingAccountMappingRows_Key",
                schema: "accounting",
                table: "PostingAccountMappingRows",
                sql: "([Purpose] = N'sales-invoice' AND [MappingKey] IN (N'receivable', N'revenue', N'vat-output', N'withholding-receivable')) OR ([Purpose] = N'purchase-invoice' AND [MappingKey] IN (N'payable', N'inventory', N'vat-input', N'withholding-payable', N'expense'))");
        }
    }
}
