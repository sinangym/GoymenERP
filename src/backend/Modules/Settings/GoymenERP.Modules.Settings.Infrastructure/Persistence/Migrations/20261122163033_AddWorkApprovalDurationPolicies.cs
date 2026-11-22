using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkApprovalDurationPolicies : Migration
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Key",
                schema: "settings",
                table: "CompanyPolicyVersions",
                sql: "[PolicyKey] IN ('finance.opening-period', 'manufacturing.due-date-buffer', 'sales.subcontract-tax-default', 'purchasing.subcontract-tax-default', 'sales.contract-discount-limit', 'manufacturing.include-scrap-cost', 'work.notification-active-days', 'work.approval-duration-hours', 'work.approval-duration-business-days')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Value",
                schema: "settings",
                table: "CompanyPolicyVersions",
                sql: "([PolicyKey] IN ('finance.opening-period', 'manufacturing.include-scrap-cost') AND [PercentValue] IS NULL AND [BooleanValue] IS NOT NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] IN ('manufacturing.due-date-buffer', 'work.notification-active-days') AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NOT NULL AND [WholeNumberValue] >= 0 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] IN ('work.approval-duration-hours', 'work.approval-duration-business-days') AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NOT NULL AND [WholeNumberValue] >= 1 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] IN ('sales.subcontract-tax-default', 'purchasing.subcontract-tax-default') AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NOT NULL AND [WithholdingApplies] IS NOT NULL AND (([VatApplies] = 1 AND [VatPercent] IS NOT NULL AND [VatPercent] > 0 AND [VatPercent] <= 100 AND [ExemptionCode] IS NULL) OR ([VatApplies] = 0 AND [VatPercent] IS NULL AND [WithholdingApplies] = 0)) AND (([WithholdingApplies] = 1 AND [WithholdingPercent] IS NOT NULL AND [WithholdingPercent] > 0 AND [WithholdingPercent] <= 100) OR ([WithholdingApplies] = 0 AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL))) OR ([PolicyKey] = 'sales.contract-discount-limit' AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [PercentValue] IS NOT NULL AND [PercentValue] >= 0 AND [PercentValue] <= 100 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // W1-A forward fix (Work-owned policy keys, Work THROW range): recorded approval durations would break the
            // narrowed key check; an empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[CompanyPolicyVersions]
                           WHERE [PolicyKey] IN ('work.approval-duration-hours', 'work.approval-duration-business-days'))
                BEGIN
                    THROW 52084, 'Work approval duration policy versions exist; AddWorkApprovalDurationPolicies is not rolled back (forward fix).', 1;
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Key",
                schema: "settings",
                table: "CompanyPolicyVersions",
                sql: "[PolicyKey] IN ('finance.opening-period', 'manufacturing.due-date-buffer', 'sales.subcontract-tax-default', 'purchasing.subcontract-tax-default', 'sales.contract-discount-limit', 'manufacturing.include-scrap-cost', 'work.notification-active-days')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Value",
                schema: "settings",
                table: "CompanyPolicyVersions",
                sql: "([PolicyKey] IN ('finance.opening-period', 'manufacturing.include-scrap-cost') AND [PercentValue] IS NULL AND [BooleanValue] IS NOT NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] IN ('manufacturing.due-date-buffer', 'work.notification-active-days') AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NOT NULL AND [WholeNumberValue] >= 0 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] IN ('sales.subcontract-tax-default', 'purchasing.subcontract-tax-default') AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NOT NULL AND [WithholdingApplies] IS NOT NULL AND (([VatApplies] = 1 AND [VatPercent] IS NOT NULL AND [VatPercent] > 0 AND [VatPercent] <= 100 AND [ExemptionCode] IS NULL) OR ([VatApplies] = 0 AND [VatPercent] IS NULL AND [WithholdingApplies] = 0)) AND (([WithholdingApplies] = 1 AND [WithholdingPercent] IS NOT NULL AND [WithholdingPercent] > 0 AND [WithholdingPercent] <= 100) OR ([WithholdingApplies] = 0 AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL))) OR ([PolicyKey] = 'sales.contract-discount-limit' AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [PercentValue] IS NOT NULL AND [PercentValue] >= 0 AND [PercentValue] <= 100 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL)");
        }
    }
}
