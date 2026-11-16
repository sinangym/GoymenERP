using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneyProfileVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoneyProfileBaselineTransfers",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneyProfileBaselineTransfers", x => new { x.CompanyId, x.CurrencyId, x.Operation });
                    table.CheckConstraint("CK_SettingsMoneyProfileBaselineTransfers_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND LEN(LTRIM(RTRIM([Operation]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "MoneyProfileVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Scale = table.Column<int>(type: "int", nullable: false),
                    UnitPriceScale = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    DecisionReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneyProfileVersions", x => new { x.CompanyId, x.CurrencyId, x.Operation, x.Version });
                    table.CheckConstraint("CK_SettingsMoneyProfileVersions_Codes", "LEN(LTRIM(RTRIM([Operation]))) > 0 AND LEN(LTRIM(RTRIM([CurrencyCode]))) > 0");
                    table.CheckConstraint("CK_SettingsMoneyProfileVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsMoneyProfileVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsMoneyProfileVersions_Scale", "[Scale] BETWEEN 0 AND 28 AND ([UnitPriceScale] IS NULL OR [UnitPriceScale] BETWEEN 0 AND 28)");
                    table.CheckConstraint("CK_SettingsMoneyProfileVersions_Source", "[SourceKind] IN ('CompiledBaselineV0','UserMaintained')");
                    table.ForeignKey(
                        name: "FK_MoneyProfileVersions_MoneyProfileBaselineTransfers_CompanyId_CurrencyId_Operation",
                        columns: x => new { x.CompanyId, x.CurrencyId, x.Operation },
                        principalSchema: "settings",
                        principalTable: "MoneyProfileBaselineTransfers",
                        principalColumns: new[] { "CompanyId", "CurrencyId", "Operation" },
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[MoneyProfileVersions])
                    OR EXISTS (SELECT 1 FROM [settings].[MoneyProfileBaselineTransfers])
                BEGIN
                    THROW 51940, 'Money profile versions exist; AddMoneyProfileVersions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "MoneyProfileVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "MoneyProfileBaselineTransfers",
                schema: "settings");
        }
    }
}
