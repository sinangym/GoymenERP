using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowCashExchangeJournalSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceJournalSources_Source",
                schema: "accounting",
                table: "FinanceJournalSources");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceJournalSources_Source",
                schema: "accounting",
                table: "FinanceJournalSources",
                sql: "[SourceKind] IN (N'finance.expense-payment.recorded.v1', N'finance.cash-transfer.recorded.v1', N'finance.cash-exchange.recorded.v1', N'finance.cash-exchange.book-value-approved.v1') AND LEN([SourceVersion]) > 0 AND LEN([OwnerProfileId]) > 0 AND LEN([OwnerProfileVersion]) > 0 AND LEN([AmountText]) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[FinanceJournalSources]
                           WHERE [SourceKind] IN (N'finance.cash-exchange.recorded.v1', N'finance.cash-exchange.book-value-approved.v1'))
                BEGIN
                    THROW 51810, 'Cash exchange source evidence exists; AllowCashExchangeJournalSources is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinanceJournalSources_Source",
                schema: "accounting",
                table: "FinanceJournalSources");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinanceJournalSources_Source",
                schema: "accounting",
                table: "FinanceJournalSources",
                sql: "[SourceKind] IN (N'finance.expense-payment.recorded.v1', N'finance.cash-transfer.recorded.v1') AND LEN([SourceVersion]) > 0 AND LEN([OwnerProfileId]) > 0 AND LEN([OwnerProfileVersion]) > 0 AND LEN([AmountText]) > 0");
        }
    }
}
