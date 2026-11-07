using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenItemInvoiceExchange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateText",
                schema: "finance",
                table: "OpenItems",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FunctionalAmountText",
                schema: "finance",
                table: "OpenItems",
                type: "varchar(max)",
                unicode: false,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OpenItems_Exchange",
                schema: "finance",
                table: "OpenItems",
                sql: "([ExchangeRateText] IS NULL AND [FunctionalAmountText] IS NULL) OR ([ExchangeRateText] IS NOT NULL AND [FunctionalAmountText] IS NOT NULL AND LEN([ExchangeRateText]) > 0 AND LEN([FunctionalAmountText]) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Finance's module refusal (51051): a rollback never drops a stored functional-currency amount.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[OpenItems] WHERE [ExchangeRateText] IS NOT NULL OR [FunctionalAmountText] IS NOT NULL)
                    THROW 51051, 'Finance open items carry exchange data; AddOpenItemInvoiceExchange is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_OpenItems_Exchange",
                schema: "finance",
                table: "OpenItems");

            migrationBuilder.DropColumn(
                name: "ExchangeRateText",
                schema: "finance",
                table: "OpenItems");

            migrationBuilder.DropColumn(
                name: "FunctionalAmountText",
                schema: "finance",
                table: "OpenItems");
        }
    }
}
