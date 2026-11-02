using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// DEC-FIN-010 / KUR_GUNU / SAT-D169…D171: the company's append-only exchange-rate record (day fence, records, rate lines).
    /// <c>Down</c> refuses (<c>THROW 51201</c>, used by no other migration on main or any remote branch at the time of writing)
    /// while any rate record exists — dropping it would lose the official answers and audited manual corrections that issued
    /// documents' rate snapshots point at (forward fix); on an empty store the tables drop freely.
    /// </summary>
    public partial class AddExchangeRateRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeRateDays",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastRecordVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateDays", x => new { x.CompanyId, x.Date });
                    table.CheckConstraint("CK_SettingsExchangeRateDays_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsExchangeRateDays_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsExchangeRateDays_Version", "[Version] > 0 AND [LastRecordVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRateRecords",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordVersion = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Provenance = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    State = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Source = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateRecords", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SettingsExchangeRateRecords_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsExchangeRateRecords_ManualReason", "[Provenance] <> 'Manual' OR LEN(LTRIM(RTRIM(ISNULL([Reason], N'')))) > 0");
                    table.CheckConstraint("CK_SettingsExchangeRateRecords_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsExchangeRateRecords_Source", "[Source] = 'Tcmb'");
                    table.CheckConstraint("CK_SettingsExchangeRateRecords_State", "([Kind] = 'Bulletin' AND [Provenance] = 'Official' AND [State] IN ('Published', 'NoBulletin', 'Unverified')) OR ([Kind] = 'Bulletin' AND [Provenance] = 'Manual' AND [State] = 'Published') OR ([Kind] = 'HalfDay' AND [Provenance] = 'Official' AND [State] IN ('FullDay', 'HalfDay', 'Undetermined'))");
                    table.CheckConstraint("CK_SettingsExchangeRateRecords_Version", "[RecordVersion] > 0");
                    table.ForeignKey(
                        name: "FK_ExchangeRateRecords_ExchangeRateDays_CompanyId_Date",
                        columns: x => new { x.CompanyId, x.Date },
                        principalSchema: "settings",
                        principalTable: "ExchangeRateDays",
                        principalColumns: new[] { "CompanyId", "Date" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRateRecordRates",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyCode = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SellingRate = table.Column<decimal>(type: "decimal(28,10)", precision: 28, scale: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateRecordRates", x => new { x.CompanyId, x.RecordId, x.CurrencyCode });
                    table.CheckConstraint("CK_SettingsExchangeRateRecordRates_Currency", "LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%' AND [CurrencyCode] <> 'TRY'");
                    table.CheckConstraint("CK_SettingsExchangeRateRecordRates_Rate", "[SellingRate] > 0");
                    table.ForeignKey(
                        name: "FK_ExchangeRateRecordRates_ExchangeRateRecords_CompanyId_RecordId",
                        columns: x => new { x.CompanyId, x.RecordId },
                        principalSchema: "settings",
                        principalTable: "ExchangeRateRecords",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRateRecords_CompanyId_Date_RecordVersion",
                schema: "settings",
                table: "ExchangeRateRecords",
                columns: new[] { "CompanyId", "Date", "RecordVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[ExchangeRateDays])
                    THROW 51201, 'Settings exchange-rate records exist; AddExchangeRateRecords is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "ExchangeRateRecordRates",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "ExchangeRateRecords",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "ExchangeRateDays",
                schema: "settings");
        }
    }
}
