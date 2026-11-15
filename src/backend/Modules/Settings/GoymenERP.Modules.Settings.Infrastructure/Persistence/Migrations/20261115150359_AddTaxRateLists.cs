using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxRateLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxRateLists",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastListVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRateLists", x => new { x.CompanyId, x.Kind });
                    table.CheckConstraint("CK_SettingsTaxRateLists_Actor", "LEN(LTRIM(RTRIM([UpdatedBy]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxRateLists_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsTaxRateLists_Kind", "[Kind] IN ('VatRate', 'WithholdingRatio')");
                    table.CheckConstraint("CK_SettingsTaxRateLists_Version", "[Version] > 0 AND [LastListVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "TaxRateListVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRateListVersions", x => new { x.CompanyId, x.Kind, x.ListVersion });
                    table.CheckConstraint("CK_SettingsTaxRateListVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxRateListVersions_Version", "[ListVersion] > 0");
                    table.ForeignKey(
                        name: "FK_TaxRateListVersions_TaxRateLists_CompanyId_Kind",
                        columns: x => new { x.CompanyId, x.Kind },
                        principalSchema: "settings",
                        principalTable: "TaxRateLists",
                        principalColumns: new[] { "CompanyId", "Kind" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxRateListEntries",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    TaxCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRateListEntries", x => new { x.CompanyId, x.Kind, x.ListVersion, x.Code });
                    table.CheckConstraint("CK_SettingsTaxRateListEntries_Code", "LEN([Code]) BETWEEN 1 AND 16 AND [Code] NOT LIKE '%[^A-Z0-9/]%'");
                    table.CheckConstraint("CK_SettingsTaxRateListEntries_KindRule", "([Kind] = 'VatRate' AND [TaxCode] IS NULL) OR ([Kind] = 'WithholdingRatio' AND [Percent] > 0)");
                    table.CheckConstraint("CK_SettingsTaxRateListEntries_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxRateListEntries_Percent", "[Percent] >= 0 AND [Percent] <= 100");
                    table.CheckConstraint("CK_SettingsTaxRateListEntries_TaxCode", "[TaxCode] IS NULL OR (LEN([TaxCode]) BETWEEN 1 AND 16 AND [TaxCode] NOT LIKE '%[^A-Z0-9]%')");
                    table.ForeignKey(
                        name: "FK_TaxRateListEntries_TaxRateListVersions_CompanyId_Kind_ListVersion",
                        columns: x => new { x.CompanyId, x.Kind, x.ListVersion },
                        principalSchema: "settings",
                        principalTable: "TaxRateListVersions",
                        principalColumns: new[] { "CompanyId", "Kind", "ListVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxRateListWithdrawals",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    ReplacedByListVersion = table.Column<long>(type: "bigint", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WithdrawnBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRateListWithdrawals", x => new { x.CompanyId, x.Kind, x.ListVersion });
                    table.CheckConstraint("CK_SettingsTaxRateListWithdrawals_Recorded", "LEN(LTRIM(RTRIM([WithdrawnBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsTaxRateListWithdrawals_Replacement", "[ReplacedByListVersion] IS NULL OR [ReplacedByListVersion] > [ListVersion]");
                    table.ForeignKey(
                        name: "FK_TaxRateListWithdrawals_TaxRateListVersions_CompanyId_Kind_ListVersion",
                        columns: x => new { x.CompanyId, x.Kind, x.ListVersion },
                        principalSchema: "settings",
                        principalTable: "TaxRateListVersions",
                        principalColumns: new[] { "CompanyId", "Kind", "ListVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaxRateListWithdrawals_TaxRateListVersions_CompanyId_Kind_ReplacedByListVersion",
                        columns: x => new { x.CompanyId, x.Kind, x.ReplacedByListVersion },
                        principalSchema: "settings",
                        principalTable: "TaxRateListVersions",
                        principalColumns: new[] { "CompanyId", "Kind", "ListVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRateListWithdrawals_CompanyId_Kind_ReplacedByListVersion",
                schema: "settings",
                table: "TaxRateListWithdrawals",
                columns: new[] { "CompanyId", "Kind", "ReplacedByListVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: once a rate list is entered it is the company's only source of the VAT rates / withholding ratios a
            // Sales line picks from (SAT-D181; nothing is seeded, SAT-D148); dropping it would silently leave every line without
            // a list. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[TaxRateLists])
                    OR EXISTS (SELECT 1 FROM [settings].[TaxRateListVersions])
                    OR EXISTS (SELECT 1 FROM [settings].[TaxRateListEntries])
                    OR EXISTS (SELECT 1 FROM [settings].[TaxRateListWithdrawals])
                BEGIN
                    THROW 51377, 'Settings tax rate list rows exist; AddTaxRateLists is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "TaxRateListEntries",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxRateListWithdrawals",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxRateListVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "TaxRateLists",
                schema: "settings");
        }
    }
}
