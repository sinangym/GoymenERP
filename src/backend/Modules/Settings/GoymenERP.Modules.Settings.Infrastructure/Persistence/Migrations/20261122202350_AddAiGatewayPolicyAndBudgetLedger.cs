using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiGatewayPolicyAndBudgetLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiBudgetMonthTotals",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetMonth = table.Column<int>(type: "int", nullable: false),
                    CommittedUnits = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiBudgetMonthTotals", x => new { x.CompanyId, x.BudgetMonth });
                    table.CheckConstraint("CK_SettingsAiBudgetMonthTotals_Total", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [BudgetMonth] BETWEEN 200001 AND 210012 AND [BudgetMonth] % 100 BETWEEN 1 AND 12 AND [CommittedUnits] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "AiGatewayPolicyVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    MonthlyUsageCeiling = table.Column<long>(type: "bigint", nullable: true),
                    MaxUnitsPerRequest = table.Column<int>(type: "int", nullable: true),
                    FeatureCount = table.Column<int>(type: "int", nullable: false),
                    RouteCount = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGatewayPolicyVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_SettingsAiGatewayPolicyVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND ([MonthlyUsageCeiling] IS NULL OR [MonthlyUsageCeiling] BETWEEN 0 AND 1000000000000) AND ([MaxUnitsPerRequest] IS NULL OR [MaxUnitsPerRequest] BETWEEN 1 AND 10000000) AND [FeatureCount] BETWEEN 0 AND 9 AND [RouteCount] BETWEEN 0 AND 24 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "AiBudgetLedgerEntries",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetMonth = table.Column<int>(type: "int", nullable: false),
                    PolicyVersion = table.Column<long>(type: "bigint", nullable: false),
                    Feature = table.Column<int>(type: "int", nullable: false),
                    TaskKind = table.Column<int>(type: "int", nullable: false),
                    Placement = table.Column<int>(type: "int", nullable: false),
                    ModelDeployment = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReservedUnits = table.Column<int>(type: "int", nullable: false),
                    UsedUnits = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SettledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiBudgetLedgerEntries", x => new { x.CompanyId, x.EntryId });
                    table.CheckConstraint("CK_SettingsAiBudgetLedgerEntries_Entry", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryId] <> '00000000-0000-0000-0000-000000000000' AND [BudgetMonth] BETWEEN 200001 AND 210012 AND [PolicyVersion] > 0 AND [Feature] BETWEEN 1 AND 9 AND [TaskKind] BETWEEN 1 AND 6 AND [Placement] IN (1, 2) AND [ReservedUnits] > 0 AND LEN(LTRIM(RTRIM([ModelDeployment]))) > 0 AND LEN(LTRIM(RTRIM([ActorId]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0 AND (([State] = 1 AND [UsedUnits] IS NULL AND [SettledAtUtc] IS NULL) OR ([State] = 2 AND [UsedUnits] IS NOT NULL AND [UsedUnits] >= 0 AND [SettledAtUtc] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_AiBudgetLedgerEntries_AiBudgetMonthTotals_CompanyId_BudgetMonth",
                        columns: x => new { x.CompanyId, x.BudgetMonth },
                        principalSchema: "settings",
                        principalTable: "AiBudgetMonthTotals",
                        principalColumns: new[] { "CompanyId", "BudgetMonth" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiGatewayFeatures",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Feature = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGatewayFeatures", x => new { x.CompanyId, x.Version, x.Feature });
                    table.CheckConstraint("CK_SettingsAiGatewayFeatures_Feature", "[Feature] BETWEEN 1 AND 9");
                    table.ForeignKey(
                        name: "FK_AiGatewayFeatures_AiGatewayPolicyVersions_CompanyId_Version",
                        columns: x => new { x.CompanyId, x.Version },
                        principalSchema: "settings",
                        principalTable: "AiGatewayPolicyVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiGatewayModelRoutes",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    TaskKind = table.Column<int>(type: "int", nullable: false),
                    DataClass = table.Column<int>(type: "int", nullable: false),
                    Placement = table.Column<int>(type: "int", nullable: false),
                    ModelDeployment = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGatewayModelRoutes", x => new { x.CompanyId, x.Version, x.TaskKind, x.DataClass });
                    table.CheckConstraint("CK_SettingsAiGatewayModelRoutes_Route", "[TaskKind] BETWEEN 1 AND 6 AND [DataClass] BETWEEN 1 AND 4 AND [Placement] IN (1, 2) AND ([Placement] = 2 OR [DataClass] <= 2) AND LEN(LTRIM(RTRIM([ModelDeployment]))) > 0");
                    table.ForeignKey(
                        name: "FK_AiGatewayModelRoutes_AiGatewayPolicyVersions_CompanyId_Version",
                        columns: x => new { x.CompanyId, x.Version },
                        principalSchema: "settings",
                        principalTable: "AiGatewayPolicyVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiBudgetLedgerEntries_CompanyId_BudgetMonth",
                schema: "settings",
                table: "AiBudgetLedgerEntries",
                columns: new[] { "CompanyId", "BudgetMonth" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // WP-A1 forward fix (WP-A1 THROW range 52180-52189): recorded AI gateway policy versions or usage ledger rows
            // would be lost; an empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[AiGatewayPolicyVersions])
                   OR EXISTS (SELECT 1 FROM [settings].[AiBudgetLedgerEntries])
                   OR EXISTS (SELECT 1 FROM [settings].[AiBudgetMonthTotals])
                BEGIN
                    THROW 52180, 'AI gateway policy versions or usage ledger rows exist; AddAiGatewayPolicyAndBudgetLedger is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "AiBudgetLedgerEntries",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "AiGatewayFeatures",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "AiGatewayModelRoutes",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "AiBudgetMonthTotals",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "AiGatewayPolicyVersions",
                schema: "settings");
        }
    }
}
