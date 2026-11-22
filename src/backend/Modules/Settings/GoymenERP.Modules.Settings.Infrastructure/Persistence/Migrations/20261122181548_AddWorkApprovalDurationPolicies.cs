using System;
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

            migrationBuilder.CreateTable(
                name: "WorkHolidayCalendarVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    DayCount = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkHolidayCalendarVersions", x => new { x.CompanyId, x.Year, x.Version });
                    table.CheckConstraint("CK_SettingsWorkHolidayCalendarVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [Year] BETWEEN 2000 AND 2100 AND [DayCount] BETWEEN 0 AND 60 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "WorkHolidayCalendarDays",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkHolidayCalendarDays", x => new { x.CompanyId, x.Year, x.Version, x.Date });
                    table.CheckConstraint("CK_SettingsWorkHolidayCalendarDays_Day", "[Kind] IN (1, 2) AND YEAR([Date]) = [Year]");
                    table.ForeignKey(
                        name: "FK_WorkHolidayCalendarDays_WorkHolidayCalendarVersions_CompanyId_Year_Version",
                        columns: x => new { x.CompanyId, x.Year, x.Version },
                        principalSchema: "settings",
                        principalTable: "WorkHolidayCalendarVersions",
                        principalColumns: new[] { "CompanyId", "Year", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

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
            // W1-A forward fix (Work-owned policy keys and holiday calendars, Work THROW range): recorded approval durations
            // or holiday calendar versions would be lost or break the narrowed key check; an empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[CompanyPolicyVersions]
                           WHERE [PolicyKey] IN ('work.approval-duration-hours', 'work.approval-duration-business-days'))
                   OR EXISTS (SELECT 1 FROM [settings].[WorkHolidayCalendarVersions])
                BEGIN
                    THROW 52084, 'Work approval duration or holiday calendar versions exist; AddWorkApprovalDurationPolicies is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "WorkHolidayCalendarDays",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "WorkHolidayCalendarVersions",
                schema: "settings");

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
