using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkCenterCapacityVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkCenterCapacityVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputCapacityKgPerHour = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SafetyBufferValue = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    SafetyBufferUnit = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCenterCapacityVersions", x => new { x.CompanyId, x.WorkCenterId, x.Version });
                    table.CheckConstraint("CK_SettingsWorkCenterCapacityVersions_Capacity", "[InputCapacityKgPerHour] > 0");
                    table.CheckConstraint("CK_SettingsWorkCenterCapacityVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [WorkCenterId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsWorkCenterCapacityVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsWorkCenterCapacityVersions_SafetyBuffer", "([SafetyBufferValue] IS NULL AND [SafetyBufferUnit] IS NULL) OR ([SafetyBufferValue] > 0 AND [SafetyBufferUnit] IN ('Hours','Days'))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenterCapacityVersions_ProfileId",
                schema: "settings",
                table: "WorkCenterCapacityVersions",
                column: "ProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: once recorded, a capacity version is the work center's audited capacity and due-date buffer
            // (URT-D89); dropping it would silently turn the due-date suggestion back to "not computable" and lose its
            // history. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[WorkCenterCapacityVersions])
                BEGIN
                    THROW 51333, 'Work center capacity versions exist; AddWorkCenterCapacityVersions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "WorkCenterCapacityVersions",
                schema: "settings");
        }
    }
}
