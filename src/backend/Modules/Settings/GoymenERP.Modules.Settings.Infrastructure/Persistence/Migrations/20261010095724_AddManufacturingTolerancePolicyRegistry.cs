using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingTolerancePolicyRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "settings");

            migrationBuilder.CreateTable(
                name: "ManufacturingTolerancePolicies",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Process = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    MaximumFirePercent = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    ApprovalStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingTolerancePolicies", x => new { x.CompanyId, x.ProfileId });
                    table.CheckConstraint("CK_SettingsMfgTolerance_Approval", "[ApprovalStatus] = 'Approved' AND LEN(LTRIM(RTRIM([ApprovedBy]))) > 0 AND [ApprovedAtUtc] > '0001-01-01T00:00:00+00:00' AND LEN(LTRIM(RTRIM([ApprovalReason]))) > 0");
                    table.CheckConstraint("CK_SettingsMfgTolerance_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsMfgTolerance_Percent", "[MaximumFirePercent] >= 0");
                    table.CheckConstraint("CK_SettingsMfgTolerance_Process", "[Process] IN ('Slitting','ColdRolling','TubeProfile','Paint','CutToLength')");
                    table.CheckConstraint("CK_SettingsMfgTolerance_Profile", "[ProfileId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsMfgTolerance_Version", "[Version] > 0");
                    table.CheckConstraint("CK_SettingsMfgTolerance_Window", "[EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingTolerancePolicies_CompanyId_Process_EffectiveFrom",
                schema: "settings",
                table: "ManufacturingTolerancePolicies",
                columns: new[] { "CompanyId", "Process", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingTolerancePolicies_CompanyId_Process_Version",
                schema: "settings",
                table: "ManufacturingTolerancePolicies",
                columns: new[] { "CompanyId", "Process", "Version" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [settings].[TR_ManufacturingTolerancePolicies_NoOverlap]
                ON [settings].[ManufacturingTolerancePolicies]
                AFTER INSERT, UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                    BEGIN
                        THROW 51002, 'settings_manufacturing_tolerance_profile_immutable', 1;
                    END
                    IF EXISTS (
                        SELECT 1
                        FROM inserted AS incoming
                        INNER JOIN [settings].[ManufacturingTolerancePolicies] AS existing
                            ON existing.[CompanyId] = incoming.[CompanyId]
                            AND existing.[Process] = incoming.[Process]
                            AND existing.[ProfileId] <> incoming.[ProfileId]
                            AND existing.[EffectiveFrom] <= ISNULL(incoming.[EffectiveThrough], CONVERT(date, '99991231'))
                            AND incoming.[EffectiveFrom] <= ISNULL(existing.[EffectiveThrough], CONVERT(date, '99991231')))
                    BEGIN
                        THROW 51001, 'settings_manufacturing_tolerance_window_overlap', 1;
                    END
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [settings].[ManufacturingTolerancePolicies])
                    THROW 51003, 'settings_manufacturing_tolerance_history_exists', 1;
                """);
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS [settings].[TR_ManufacturingTolerancePolicies_NoOverlap]");
            migrationBuilder.DropTable(
                name: "ManufacturingTolerancePolicies",
                schema: "settings");
        }
    }
}
