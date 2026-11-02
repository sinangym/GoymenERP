using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingToleranceLineScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkCenterId",
                schema: "settings",
                table: "ManufacturingToleranceProfileVersions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkCenterId",
                schema: "settings",
                table: "ManufacturingTolerancePolicies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsMfgToleranceVersions_WorkCenter",
                schema: "settings",
                table: "ManufacturingToleranceProfileVersions",
                sql: "[WorkCenterId] IS NULL OR [WorkCenterId] <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SettingsMfgTolerance_WorkCenter",
                schema: "settings",
                table: "ManufacturingTolerancePolicies",
                sql: "[WorkCenterId] IS NULL OR [WorkCenterId] <> '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsMfgToleranceVersions_WorkCenter",
                schema: "settings",
                table: "ManufacturingToleranceProfileVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SettingsMfgTolerance_WorkCenter",
                schema: "settings",
                table: "ManufacturingTolerancePolicies");

            migrationBuilder.DropColumn(
                name: "WorkCenterId",
                schema: "settings",
                table: "ManufacturingToleranceProfileVersions");

            migrationBuilder.DropColumn(
                name: "WorkCenterId",
                schema: "settings",
                table: "ManufacturingTolerancePolicies");
        }
    }
}
