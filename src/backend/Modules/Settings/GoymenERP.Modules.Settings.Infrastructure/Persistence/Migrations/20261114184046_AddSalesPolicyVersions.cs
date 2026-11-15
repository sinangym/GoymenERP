using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPolicyVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesApprovalAmountThresholdVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ThresholdAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    ThresholdAmountScale = table.Column<byte>(type: "tinyint", nullable: true),
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
                    table.PrimaryKey("PK_SalesApprovalAmountThresholdVersions", x => new { x.CompanyId, x.CurrencyId, x.Version });
                    table.CheckConstraint("CK_SettingsSalesApprovalAmountThresholdVersions_Amount", "([IsEnabled] = 1 AND [ThresholdAmount] IS NOT NULL AND [ThresholdAmount] > 0 AND [ThresholdAmountScale] IS NOT NULL AND [ThresholdAmountScale] <= 8) OR ([IsEnabled] = 0 AND [ThresholdAmount] IS NULL AND [ThresholdAmountScale] IS NULL)");
                    table.CheckConstraint("CK_SettingsSalesApprovalAmountThresholdVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsSalesApprovalAmountThresholdVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsSalesApprovalAmountThresholdVersions_Source", "[SourceKind] IN ('CompiledBaselineV0','UserMaintained')");
                });

            migrationBuilder.CreateTable(
                name: "SalesDeliveryToleranceVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarnPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    ApprovalPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    BlockPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
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
                    table.PrimaryKey("PK_SalesDeliveryToleranceVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_SettingsSalesDeliveryToleranceVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsSalesDeliveryToleranceVersions_Percents", "[WarnPercent] >= 0 AND [WarnPercent] <= [ApprovalPercent] AND [ApprovalPercent] <= [BlockPercent] AND [BlockPercent] <= 100");
                    table.CheckConstraint("CK_SettingsSalesDeliveryToleranceVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsSalesDeliveryToleranceVersions_Source", "[SourceKind] IN ('CompiledBaselineV0','UserMaintained')");
                });

            migrationBuilder.CreateTable(
                name: "SalesPolicyBaselineTransfers",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyFamily = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    EntryCount = table.Column<int>(type: "int", nullable: false),
                    TransferredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TransferredBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesPolicyBaselineTransfers", x => new { x.CompanyId, x.PolicyFamily });
                    table.CheckConstraint("CK_SettingsSalesPolicyBaselineTransfers_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsSalesPolicyBaselineTransfers_EntryCount", "[EntryCount] >= 0");
                    table.CheckConstraint("CK_SettingsSalesPolicyBaselineTransfers_Family", "[PolicyFamily] IN ('DeliveryTolerance','ApprovalAmountThreshold')");
                    table.CheckConstraint("CK_SettingsSalesPolicyBaselineTransfers_Recorded", "LEN(LTRIM(RTRIM([TransferredBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesApprovalAmountThresholdVersions_CompanyId_ProfileId",
                schema: "settings",
                table: "SalesApprovalAmountThresholdVersions",
                columns: new[] { "CompanyId", "ProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveryToleranceVersions_CompanyId_ProfileId",
                schema: "settings",
                table: "SalesDeliveryToleranceVersions",
                columns: new[] { "CompanyId", "ProfileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a transfer marker or any version is a company's only policy source once written; dropping it
            // would silently send the company back to the compiled catalog. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[SalesPolicyBaselineTransfers])
                    OR EXISTS (SELECT 1 FROM [settings].[SalesDeliveryToleranceVersions])
                    OR EXISTS (SELECT 1 FROM [settings].[SalesApprovalAmountThresholdVersions])
                BEGIN
                    THROW 51311, 'Settings sales policy versions or baseline transfers exist; AddSalesPolicyVersions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "SalesApprovalAmountThresholdVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "SalesDeliveryToleranceVersions",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "SalesPolicyBaselineTransfers",
                schema: "settings");
        }
    }
}
