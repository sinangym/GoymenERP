using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingMonthlyScrapPriceVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // URT-D72: a month keeps every version of its scrap price. The rows stored before this migration are the
            // months' first entries, so the new column starts them at version 1 (no reason), which the version check
            // admits. Pure DDL; no row is rewritten.
            migrationBuilder.DropIndex(
                name: "IX_MonthlyScrapPrices_CompanyId_Year_Month",
                schema: "manufacturing",
                table: "MonthlyScrapPrices");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "manufacturing",
                table: "MonthlyScrapPrices",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "manufacturing",
                table: "MonthlyScrapPrices",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyScrapPrices_CompanyId_Year_Month_Version",
                schema: "manufacturing",
                table: "MonthlyScrapPrices",
                columns: new[] { "CompanyId", "Year", "Month", "Version" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MonthlyScrapPrices_Version",
                schema: "manufacturing",
                table: "MonthlyScrapPrices",
                sql: "([Version] = 1 AND [Reason] IS NULL) OR ([Version] > 1 AND [Reason] IS NOT NULL AND LEN([Reason]) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // URT-D72: a correction is a second (or later) version of a month with its reason. The earlier schema holds
            // one price per month and no reason, so dropping the version would lose the correction or break the month's
            // unique index; Down refuses (THROW 51627) while any correction is stored (forward fix). A store with only
            // first entries rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[MonthlyScrapPrices] WHERE [Version] > 1)
                    THROW 51627, 'A Manufacturing scrap price was corrected; AddManufacturingMonthlyScrapPriceVersions is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_MonthlyScrapPrices_CompanyId_Year_Month_Version",
                schema: "manufacturing",
                table: "MonthlyScrapPrices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MonthlyScrapPrices_Version",
                schema: "manufacturing",
                table: "MonthlyScrapPrices");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "manufacturing",
                table: "MonthlyScrapPrices");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "manufacturing",
                table: "MonthlyScrapPrices");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyScrapPrices_CompanyId_Year_Month",
                schema: "manufacturing",
                table: "MonthlyScrapPrices",
                columns: new[] { "CompanyId", "Year", "Month" },
                unique: true);
        }
    }
}
