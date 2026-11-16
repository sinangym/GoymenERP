using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyPolicyVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyPolicyVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    WholeNumberValue = table.Column<int>(type: "int", nullable: true),
                    VatApplies = table.Column<bool>(type: "bit", nullable: true),
                    VatPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    ExemptionCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true, collation: "Latin1_General_100_BIN2"),
                    WithholdingApplies = table.Column<bool>(type: "bit", nullable: true),
                    WithholdingPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    WithholdingCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyPolicyVersions", x => new { x.CompanyId, x.PolicyKey, x.Version });
                    table.CheckConstraint("CK_SettingsCompanyPolicyVersions_Codes", "([ExemptionCode] IS NULL OR (LEN([ExemptionCode]) BETWEEN 1 AND 16 AND [ExemptionCode] NOT LIKE '%[^A-Z0-9]%')) AND ([WithholdingCode] IS NULL OR (LEN([WithholdingCode]) BETWEEN 1 AND 16 AND [WithholdingCode] NOT LIKE '%[^A-Z0-9]%'))");
                    table.CheckConstraint("CK_SettingsCompanyPolicyVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsCompanyPolicyVersions_Key", "[PolicyKey] IN ('finance.opening-period', 'manufacturing.due-date-buffer', 'sales.subcontract-tax-default', 'purchasing.subcontract-tax-default')");
                    table.CheckConstraint("CK_SettingsCompanyPolicyVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsCompanyPolicyVersions_Value", "([PolicyKey] = 'finance.opening-period' AND [BooleanValue] IS NOT NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] = 'manufacturing.due-date-buffer' AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NOT NULL AND [WholeNumberValue] >= 0 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] IN ('sales.subcontract-tax-default', 'purchasing.subcontract-tax-default') AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NOT NULL AND [WithholdingApplies] IS NOT NULL AND (([VatApplies] = 1 AND [VatPercent] IS NOT NULL AND [VatPercent] > 0 AND [VatPercent] <= 100 AND [ExemptionCode] IS NULL) OR ([VatApplies] = 0 AND [VatPercent] IS NULL AND [WithholdingApplies] = 0)) AND (([WithholdingApplies] = 1 AND [WithholdingPercent] IS NOT NULL AND [WithholdingPercent] > 0 AND [WithholdingPercent] <= 100) OR ([WithholdingApplies] = 0 AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL)))");
                });

            migrationBuilder.CreateTable(
                name: "FinanceCreditAccountMappingVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LedgerAccountCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceCreditAccountMappingVersions", x => new { x.CompanyId, x.Purpose, x.Version });
                    table.CheckConstraint("CK_SettingsFinanceCreditAccountMappingVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [Purpose] IN (1, 2, 3)");
                    table.CheckConstraint("CK_SettingsFinanceCreditAccountMappingVersions_Recorded", "LEN(LTRIM(RTRIM([LedgerAccountCode]))) > 0 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: dropping recorded policy or mapping versions would lose company settings history.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[CompanyPolicyVersions])
                    OR EXISTS (SELECT 1 FROM [settings].[FinanceCreditAccountMappingVersions])
                BEGIN
                    THROW 51853, 'Company policy or credit account mapping versions exist; AddCompanyPolicyVersions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CompanyPolicyVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "FinanceCreditAccountMappingVersions",
                schema: "settings");
        }
    }
}
