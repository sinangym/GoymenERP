using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesContractDiscountLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Key",
                schema: "settings",
                table: "CompanyPolicyVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Value",
                schema: "settings",
                table: "CompanyPolicyVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsCollateralTypeVersions_RateText",
                schema: "settings",
                table: "CollateralTypeVersions");

            migrationBuilder.AddColumn<decimal>(
                name: "PercentValue",
                schema: "settings",
                table: "CompanyPolicyVersions",
                type: "decimal(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Key",
                schema: "settings",
                table: "CompanyPolicyVersions",
                sql: "[PolicyKey] IN ('finance.opening-period', 'manufacturing.due-date-buffer', 'sales.subcontract-tax-default', 'purchasing.subcontract-tax-default', 'sales.contract-discount-limit')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Value",
                schema: "settings",
                table: "CompanyPolicyVersions",
                sql: "([PolicyKey] = 'finance.opening-period' AND [PercentValue] IS NULL AND [BooleanValue] IS NOT NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] = 'manufacturing.due-date-buffer' AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NOT NULL AND [WholeNumberValue] >= 0 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] IN ('sales.subcontract-tax-default', 'purchasing.subcontract-tax-default') AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NOT NULL AND [WithholdingApplies] IS NOT NULL AND (([VatApplies] = 1 AND [VatPercent] IS NOT NULL AND [VatPercent] > 0 AND [VatPercent] <= 100 AND [ExemptionCode] IS NULL) OR ([VatApplies] = 0 AND [VatPercent] IS NULL AND [WithholdingApplies] = 0)) AND (([WithholdingApplies] = 1 AND [WithholdingPercent] IS NOT NULL AND [WithholdingPercent] > 0 AND [WithholdingPercent] <= 100) OR ([WithholdingApplies] = 0 AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL))) OR ([PolicyKey] = 'sales.contract-discount-limit' AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [PercentValue] IS NOT NULL AND [PercentValue] >= 0 AND [PercentValue] <= 100 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCollateralTypeVersions_RateText",
                schema: "settings",
                table: "CollateralTypeVersions",
                sql: "LEN([RatePercent]) BETWEEN 1 AND 64 AND [RatePercent] NOT LIKE '%[^0-9.]%' AND LEFT([RatePercent], 1) <> '.' AND RIGHT([RatePercent], 1) <> '.' AND LEN([RatePercent]) - LEN(REPLACE([RatePercent], '.', '')) <= 1 AND (LEN(SUBSTRING(LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1), PATINDEX('%[^0]%', LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1) + '1'), 64)) <= 2 OR (SUBSTRING(LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1), PATINDEX('%[^0]%', LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1) + '1'), 64) = '100' AND REPLACE(SUBSTRING([RatePercent], CHARINDEX('.', [RatePercent] + '.') + 1, 64), '0', '') = ''))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[CompanyPolicyVersions]
                           WHERE [PolicyKey] = 'sales.contract-discount-limit' OR [PercentValue] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [settings].[CollateralTypeVersions]
                              WHERE CASE WHEN (TRY_CONVERT(int, LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1))
                                               BETWEEN 0 AND 99 OR [RatePercent] = '100') THEN 1 ELSE 0 END = 0)
                BEGIN
                    THROW 51942, 'Contract limits or rates incompatible with the previous catalog check exist; AddSalesContractDiscountLimit is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Key",
                schema: "settings",
                table: "CompanyPolicyVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Value",
                schema: "settings",
                table: "CompanyPolicyVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsCollateralTypeVersions_RateText",
                schema: "settings",
                table: "CollateralTypeVersions");

            migrationBuilder.DropColumn(
                name: "PercentValue",
                schema: "settings",
                table: "CompanyPolicyVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Key",
                schema: "settings",
                table: "CompanyPolicyVersions",
                sql: "[PolicyKey] IN ('finance.opening-period', 'manufacturing.due-date-buffer', 'sales.subcontract-tax-default', 'purchasing.subcontract-tax-default')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Value",
                schema: "settings",
                table: "CompanyPolicyVersions",
                sql: "([PolicyKey] = 'finance.opening-period' AND [BooleanValue] IS NOT NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] = 'manufacturing.due-date-buffer' AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NOT NULL AND [WholeNumberValue] >= 0 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] IN ('sales.subcontract-tax-default', 'purchasing.subcontract-tax-default') AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NOT NULL AND [WithholdingApplies] IS NOT NULL AND (([VatApplies] = 1 AND [VatPercent] IS NOT NULL AND [VatPercent] > 0 AND [VatPercent] <= 100 AND [ExemptionCode] IS NULL) OR ([VatApplies] = 0 AND [VatPercent] IS NULL AND [WithholdingApplies] = 0)) AND (([WithholdingApplies] = 1 AND [WithholdingPercent] IS NOT NULL AND [WithholdingPercent] > 0 AND [WithholdingPercent] <= 100) OR ([WithholdingApplies] = 0 AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCollateralTypeVersions_RateText",
                schema: "settings",
                table: "CollateralTypeVersions",
                sql: "LEN([RatePercent]) BETWEEN 1 AND 64 AND [RatePercent] NOT LIKE '%[^0-9.]%' AND LEFT([RatePercent], 1) <> '.' AND RIGHT([RatePercent], 1) <> '.' AND LEN([RatePercent]) - LEN(REPLACE([RatePercent], '.', '')) <= 1 AND (TRY_CONVERT(int, LEFT([RatePercent], CHARINDEX('.', [RatePercent] + '.') - 1)) BETWEEN 0 AND 99 OR [RatePercent] = '100')");
        }
    }
}
