using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeasingInstallmentVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VatText",
                schema: "finance",
                table: "CreditAccountInstallments",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditAccountInstallments_Vat",
                schema: "finance",
                table: "CreditAccountInstallments",
                sql: "[VatText] IS NULL OR (LEN([VatText]) > 0 AND [VatText] NOT LIKE '%[^0-9.]%' AND [VatText] NOT LIKE '%.%.%' AND [VatText] NOT LIKE '.%' AND [VatText] NOT LIKE '%.' )");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[CreditAccountInstallments] WITH (UPDLOCK, HOLDLOCK) WHERE [VatText] IS NOT NULL)
                BEGIN
                    THROW 52360, 'Source leasing VAT exists; AddLeasingInstallmentVat is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditAccountInstallments_Vat",
                schema: "finance",
                table: "CreditAccountInstallments");

            migrationBuilder.DropColumn(
                name: "VatText",
                schema: "finance",
                table: "CreditAccountInstallments");
        }
    }
}
