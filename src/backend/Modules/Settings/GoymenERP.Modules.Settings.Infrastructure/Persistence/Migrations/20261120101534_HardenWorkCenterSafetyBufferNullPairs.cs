using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenWorkCenterSafetyBufferNullPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsWorkCenterCapacityVersions_SafetyBuffer",
                schema: "settings",
                table: "WorkCenterCapacityVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsWorkCenterCapacityVersions_SafetyBuffer",
                schema: "settings",
                table: "WorkCenterCapacityVersions",
                sql: "([SafetyBufferValue] IS NULL AND [SafetyBufferUnit] IS NULL) OR ([SafetyBufferValue] IS NOT NULL AND [SafetyBufferUnit] IS NOT NULL AND [SafetyBufferValue] > 0 AND [SafetyBufferUnit] IN ('Hours','Days'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsWorkCenterCapacityVersions_SafetyBuffer",
                schema: "settings",
                table: "WorkCenterCapacityVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsWorkCenterCapacityVersions_SafetyBuffer",
                schema: "settings",
                table: "WorkCenterCapacityVersions",
                sql: "([SafetyBufferValue] IS NULL AND [SafetyBufferUnit] IS NULL) OR ([SafetyBufferValue] > 0 AND [SafetyBufferUnit] IN ('Hours','Days'))");
        }
    }
}
