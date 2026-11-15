using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesQuoteAndEInvoiceScenarioPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsSalesPolicyBaselineTransfers_Family",
                schema: "settings",
                table: "SalesPolicyBaselineTransfers");

            migrationBuilder.CreateTable(
                name: "EInvoiceScenarioCodeListVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    DecisionReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EInvoiceScenarioCodeListVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_SettingsEInvoiceScenarioCodeListVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsEInvoiceScenarioCodeListVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsEInvoiceScenarioCodeListVersions_Source", "[SourceKind] IN ('CompiledBaselineV0','UserMaintained')");
                });

            migrationBuilder.CreateTable(
                name: "EInvoiceScenarioDefaultVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    DecisionReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EInvoiceScenarioDefaultVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_SettingsEInvoiceScenarioDefaultVersions_Code", "[ScenarioCode] LIKE '[A-Z]%' AND [ScenarioCode] NOT LIKE '%[^A-Z0-9_]%'");
                    table.CheckConstraint("CK_SettingsEInvoiceScenarioDefaultVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsEInvoiceScenarioDefaultVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsEInvoiceScenarioDefaultVersions_Source", "[SourceKind] IN ('CompiledBaselineV0','UserMaintained')");
                });

            migrationBuilder.CreateTable(
                name: "SalesQuoteDefaultValidityVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidityDays = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_SalesQuoteDefaultValidityVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_SettingsSalesQuoteDefaultValidityVersions_Days", "[ValidityDays] > 0");
                    table.CheckConstraint("CK_SettingsSalesQuoteDefaultValidityVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsSalesQuoteDefaultValidityVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsSalesQuoteDefaultValidityVersions_Source", "[SourceKind] IN ('CompiledBaselineV0','UserMaintained')");
                });

            migrationBuilder.CreateTable(
                name: "SalesQuoteLossReasonListVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_SalesQuoteLossReasonListVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_SettingsSalesQuoteLossReasonListVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsSalesQuoteLossReasonListVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsSalesQuoteLossReasonListVersions_Source", "[SourceKind] IN ('CompiledBaselineV0','UserMaintained')");
                });

            migrationBuilder.CreateTable(
                name: "EInvoiceScenarioCodes",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EInvoiceScenarioCodes", x => new { x.CompanyId, x.ListVersion, x.Position });
                    table.CheckConstraint("CK_SettingsEInvoiceScenarioCodes_Code", "[Code] LIKE '[A-Z]%' AND [Code] NOT LIKE '%[^A-Z0-9_]%'");
                    table.CheckConstraint("CK_SettingsEInvoiceScenarioCodes_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ListVersion] > 0 AND [Position] >= 0");
                    table.ForeignKey(
                        name: "FK_EInvoiceScenarioCodes_EInvoiceScenarioCodeListVersions_CompanyId_ListVersion",
                        columns: x => new { x.CompanyId, x.ListVersion },
                        principalSchema: "settings",
                        principalTable: "EInvoiceScenarioCodeListVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesQuoteLossReasonEntries",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ListVersion = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    ReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequiresExplanation = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuoteLossReasonEntries", x => new { x.CompanyId, x.ListVersion, x.Position });
                    table.CheckConstraint("CK_SettingsSalesQuoteLossReasonEntries_Code", "LEN(LTRIM(RTRIM([Code]))) > 0 AND DATALENGTH([Code]) = DATALENGTH(LTRIM(RTRIM([Code])))");
                    table.CheckConstraint("CK_SettingsSalesQuoteLossReasonEntries_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ReasonId] <> '00000000-0000-0000-0000-000000000000' AND [ListVersion] > 0 AND [Position] >= 0");
                    table.CheckConstraint("CK_SettingsSalesQuoteLossReasonEntries_Label", "LEN(LTRIM(RTRIM([Label]))) > 0 AND DATALENGTH([Label]) = DATALENGTH(LTRIM(RTRIM([Label])))");
                    table.ForeignKey(
                        name: "FK_SalesQuoteLossReasonEntries_SalesQuoteLossReasonListVersions_CompanyId_ListVersion",
                        columns: x => new { x.CompanyId, x.ListVersion },
                        principalSchema: "settings",
                        principalTable: "SalesQuoteLossReasonListVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsSalesPolicyBaselineTransfers_Family",
                schema: "settings",
                table: "SalesPolicyBaselineTransfers",
                sql: "[PolicyFamily] IN ('DeliveryTolerance','ApprovalAmountThreshold','SalesQuotePolicy','EInvoiceScenario')");

            migrationBuilder.CreateIndex(
                name: "IX_EInvoiceScenarioCodeListVersions_CompanyId_ProfileId",
                schema: "settings",
                table: "EInvoiceScenarioCodeListVersions",
                columns: new[] { "CompanyId", "ProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EInvoiceScenarioCodes_CompanyId_ListVersion_Code",
                schema: "settings",
                table: "EInvoiceScenarioCodes",
                columns: new[] { "CompanyId", "ListVersion", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EInvoiceScenarioDefaultVersions_CompanyId_ProfileId",
                schema: "settings",
                table: "EInvoiceScenarioDefaultVersions",
                columns: new[] { "CompanyId", "ProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteDefaultValidityVersions_CompanyId_ProfileId",
                schema: "settings",
                table: "SalesQuoteDefaultValidityVersions",
                columns: new[] { "CompanyId", "ProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLossReasonEntries_CompanyId_ListVersion_Code",
                schema: "settings",
                table: "SalesQuoteLossReasonEntries",
                columns: new[] { "CompanyId", "ListVersion", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLossReasonEntries_CompanyId_ListVersion_ReasonId",
                schema: "settings",
                table: "SalesQuoteLossReasonEntries",
                columns: new[] { "CompanyId", "ListVersion", "ReasonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuoteLossReasonListVersions_CompanyId_ProfileId",
                schema: "settings",
                table: "SalesQuoteLossReasonListVersions",
                columns: new[] { "CompanyId", "ProfileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: once a quote / e-Fatura scenario transfer marker or version is written it is the company's only
            // source for that family; dropping it would silently send the company back to the compiled catalog, and the
            // narrowed family check could not be re-added over such a marker. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[SalesQuoteLossReasonListVersions])
                    OR EXISTS (SELECT 1 FROM [settings].[SalesQuoteLossReasonEntries])
                    OR EXISTS (SELECT 1 FROM [settings].[SalesQuoteDefaultValidityVersions])
                    OR EXISTS (SELECT 1 FROM [settings].[EInvoiceScenarioCodeListVersions])
                    OR EXISTS (SELECT 1 FROM [settings].[EInvoiceScenarioCodes])
                    OR EXISTS (SELECT 1 FROM [settings].[EInvoiceScenarioDefaultVersions])
                    OR EXISTS (SELECT 1 FROM [settings].[SalesPolicyBaselineTransfers]
                        WHERE [PolicyFamily] IN ('SalesQuotePolicy','EInvoiceScenario'))
                BEGIN
                    THROW 51312, 'Settings quote or e-Fatura scenario policy rows exist; AddSalesQuoteAndEInvoiceScenarioPolicies is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "EInvoiceScenarioCodes",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "EInvoiceScenarioDefaultVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "SalesQuoteDefaultValidityVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "SalesQuoteLossReasonEntries",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "EInvoiceScenarioCodeListVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "SalesQuoteLossReasonListVersions",
                schema: "settings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsSalesPolicyBaselineTransfers_Family",
                schema: "settings",
                table: "SalesPolicyBaselineTransfers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsSalesPolicyBaselineTransfers_Family",
                schema: "settings",
                table: "SalesPolicyBaselineTransfers",
                sql: "[PolicyFamily] IN ('DeliveryTolerance','ApprovalAmountThreshold')");
        }
    }
}
