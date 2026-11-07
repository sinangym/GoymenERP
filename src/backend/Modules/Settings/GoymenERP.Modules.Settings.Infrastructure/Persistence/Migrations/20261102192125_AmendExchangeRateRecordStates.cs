using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Review B P2 / EK31 SAT-D198: the exchange-rate record accepts the official <c>NotFound</c> bulletin state (a single 404,
    /// not yet a final "no bulletin") and the reasoned manual half-day mark (<c>HalfDay</c> / <c>Manual</c>, FullDay or HalfDay).
    /// <c>Down</c> refuses (<c>THROW 51202</c>, used by no other migration on main or any remote branch at the time of writing)
    /// while any such record exists — narrowing the check would drop recorded facts (forward fix); otherwise it restores the
    /// previous check.
    /// </summary>
    public partial class AmendExchangeRateRecordStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsExchangeRateRecords_State",
                schema: "settings",
                table: "ExchangeRateRecords");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsExchangeRateRecords_State",
                schema: "settings",
                table: "ExchangeRateRecords",
                sql: "([Kind] = 'Bulletin' AND [Provenance] = 'Official' AND [State] IN ('Published', 'NoBulletin', 'Unverified', 'NotFound')) OR ([Kind] = 'Bulletin' AND [Provenance] = 'Manual' AND [State] = 'Published') OR ([Kind] = 'HalfDay' AND [Provenance] = 'Official' AND [State] IN ('FullDay', 'HalfDay', 'Undetermined')) OR ([Kind] = 'HalfDay' AND [Provenance] = 'Manual' AND [State] IN ('FullDay', 'HalfDay'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[ExchangeRateRecords]
                           WHERE [State] = 'NotFound' OR ([Kind] = 'HalfDay' AND [Provenance] = 'Manual'))
                    THROW 51202, 'Exchange-rate NotFound or manual half-day records exist; AmendExchangeRateRecordStates is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsExchangeRateRecords_State",
                schema: "settings",
                table: "ExchangeRateRecords");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsExchangeRateRecords_State",
                schema: "settings",
                table: "ExchangeRateRecords",
                sql: "([Kind] = 'Bulletin' AND [Provenance] = 'Official' AND [State] IN ('Published', 'NoBulletin', 'Unverified')) OR ([Kind] = 'Bulletin' AND [Provenance] = 'Manual' AND [State] = 'Published') OR ([Kind] = 'HalfDay' AND [Provenance] = 'Official' AND [State] IN ('FullDay', 'HalfDay', 'Undetermined'))");
        }
    }
}
