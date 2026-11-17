using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentNumberSeriesAndOperationalPolicies : Migration
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
                name: "DocumentNumberCounters",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberCounters", x => new { x.CompanyId, x.Series, x.Year });
                    table.CheckConstraint("CK_SettingsDocumentNumberCounters_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4) AND [Year] BETWEEN 1 AND 9999 AND [LastSequence] > 0");
                });

            migrationBuilder.CreateTable(
                name: "DocumentNumberFormats",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Prefix = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SequenceDigits = table.Column<int>(type: "int", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberFormats", x => new { x.CompanyId, x.Series, x.Version });
                    table.CheckConstraint("CK_SettingsDocumentNumberFormats_Format", "LEN([Prefix]) BETWEEN 1 AND 16 AND [Prefix] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9]%' AND [SequenceDigits] BETWEEN 1 AND 18");
                    table.CheckConstraint("CK_SettingsDocumentNumberFormats_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4) AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsDocumentNumberFormats_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "DocumentNumberReservations",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    FormatVersion = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberReservations", x => new { x.CompanyId, x.Series, x.DocumentId });
                    table.CheckConstraint("CK_SettingsDocumentNumberReservations_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4) AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [Year] = YEAR([BusinessDate]) AND [Sequence] > 0 AND [FormatVersion] > 0");
                    table.CheckConstraint("CK_SettingsDocumentNumberReservations_Recorded", "LEN([Number]) > 0 AND LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0");
                    table.ForeignKey(
                        name: "FK_DocumentNumberReservations_DocumentNumberCounters_CompanyId_Series_Year",
                        columns: x => new { x.CompanyId, x.Series, x.Year },
                        principalSchema: "settings",
                        principalTable: "DocumentNumberCounters",
                        principalColumns: new[] { "CompanyId", "Series", "Year" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentNumberReservations_DocumentNumberFormats_CompanyId_Series_FormatVersion",
                        columns: x => new { x.CompanyId, x.Series, x.FormatVersion },
                        principalSchema: "settings",
                        principalTable: "DocumentNumberFormats",
                        principalColumns: new[] { "CompanyId", "Series", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentNumberCancellations",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberCancellations", x => new { x.CompanyId, x.Series, x.DocumentId });
                    table.CheckConstraint("CK_SettingsDocumentNumberCancellations_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Series] IN (1,2,3,4) AND [DocumentId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsDocumentNumberCancellations_Recorded", "LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.ForeignKey(
                        name: "FK_DocumentNumberCancellations_DocumentNumberReservations_CompanyId_Series_DocumentId",
                        columns: x => new { x.CompanyId, x.Series, x.DocumentId },
                        principalSchema: "settings",
                        principalTable: "DocumentNumberReservations",
                        principalColumns: new[] { "CompanyId", "Series", "DocumentId" },
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberCancellations_CompanyId_KeyHash",
                schema: "settings",
                table: "DocumentNumberCancellations",
                columns: new[] { "CompanyId", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberReservations_CompanyId_KeyHash",
                schema: "settings",
                table: "DocumentNumberReservations",
                columns: new[] { "CompanyId", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberReservations_CompanyId_Number",
                schema: "settings",
                table: "DocumentNumberReservations",
                columns: new[] { "CompanyId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberReservations_CompanyId_Series_FormatVersion",
                schema: "settings",
                table: "DocumentNumberReservations",
                columns: new[] { "CompanyId", "Series", "FormatVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberReservations_CompanyId_Series_Year_Sequence",
                schema: "settings",
                table: "DocumentNumberReservations",
                columns: new[] { "CompanyId", "Series", "Year", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[DocumentNumberCancellations])
                    OR EXISTS (SELECT 1 FROM [settings].[DocumentNumberReservations])
                    OR EXISTS (SELECT 1 FROM [settings].[DocumentNumberCounters])
                    OR EXISTS (SELECT 1 FROM [settings].[DocumentNumberFormats])
                    OR EXISTS (SELECT 1 FROM [settings].[CompanyPolicyVersions]
                        WHERE [PolicyKey] IN ('manufacturing.include-scrap-cost', 'work.notification-active-days'))
                BEGIN
                    THROW 51954, 'Document number or operational policy history exists; AddDocumentNumberSeriesAndOperationalPolicies is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "DocumentNumberCancellations",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "DocumentNumberReservations",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "DocumentNumberCounters",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "DocumentNumberFormats",
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
                sql: "[PolicyKey] IN ('finance.opening-period', 'manufacturing.due-date-buffer', 'sales.subcontract-tax-default', 'purchasing.subcontract-tax-default', 'sales.contract-discount-limit')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsCompanyPolicyVersions_Value",
                schema: "settings",
                table: "CompanyPolicyVersions",
                sql: "([PolicyKey] = 'finance.opening-period' AND [PercentValue] IS NULL AND [BooleanValue] IS NOT NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] = 'manufacturing.due-date-buffer' AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NOT NULL AND [WholeNumberValue] >= 0 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL) OR ([PolicyKey] IN ('sales.subcontract-tax-default', 'purchasing.subcontract-tax-default') AND [PercentValue] IS NULL AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [VatApplies] IS NOT NULL AND [WithholdingApplies] IS NOT NULL AND (([VatApplies] = 1 AND [VatPercent] IS NOT NULL AND [VatPercent] > 0 AND [VatPercent] <= 100 AND [ExemptionCode] IS NULL) OR ([VatApplies] = 0 AND [VatPercent] IS NULL AND [WithholdingApplies] = 0)) AND (([WithholdingApplies] = 1 AND [WithholdingPercent] IS NOT NULL AND [WithholdingPercent] > 0 AND [WithholdingPercent] <= 100) OR ([WithholdingApplies] = 0 AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL))) OR ([PolicyKey] = 'sales.contract-discount-limit' AND [BooleanValue] IS NULL AND [WholeNumberValue] IS NULL AND [PercentValue] IS NOT NULL AND [PercentValue] >= 0 AND [PercentValue] <= 100 AND [VatApplies] IS NULL AND [VatPercent] IS NULL AND [ExemptionCode] IS NULL AND [WithholdingApplies] IS NULL AND [WithholdingPercent] IS NULL AND [WithholdingCode] IS NULL)");
        }
    }
}
