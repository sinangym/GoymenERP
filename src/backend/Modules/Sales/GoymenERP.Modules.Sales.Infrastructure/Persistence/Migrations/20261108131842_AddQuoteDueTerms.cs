using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteDueTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DueTermsJson",
                schema: "sales",
                table: "QuoteRevisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuoteRevisions_DueTerms",
                schema: "sales",
                table: "QuoteRevisions",
                sql: "[DueTermsJson] IS NULL OR ISJSON([DueTermsJson]) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a quote revision that carries due terms ("teklif vade", SAT-D132) would lose them on rollback.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[QuoteRevisions] WHERE [DueTermsJson] IS NOT NULL)
                BEGIN
                    THROW 51227, 'A quote revision carries due terms; AddQuoteDueTerms is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_QuoteRevisions_DueTerms",
                schema: "sales",
                table: "QuoteRevisions");

            migrationBuilder.DropColumn(
                name: "DueTermsJson",
                schema: "sales",
                table: "QuoteRevisions");
        }
    }
}
