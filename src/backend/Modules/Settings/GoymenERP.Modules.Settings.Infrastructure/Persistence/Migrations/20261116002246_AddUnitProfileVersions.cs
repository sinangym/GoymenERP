using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitProfileVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UnitProfileBaselineTransfers",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryCount = table.Column<int>(type: "int", nullable: false),
                    TransferredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TransferredBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitProfileBaselineTransfers", x => x.CompanyId);
                    table.CheckConstraint("CK_SettingsUnitProfileBaselineTransfers_Company", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsUnitProfileBaselineTransfers_EntryCount", "[EntryCount] >= 0");
                    table.CheckConstraint("CK_SettingsUnitProfileBaselineTransfers_Recorded", "LEN(LTRIM(RTRIM([TransferredBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "UnitProfileVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DimensionCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsKilogram = table.Column<bool>(type: "bit", nullable: false),
                    Scale = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_UnitProfileVersions", x => new { x.CompanyId, x.UnitId, x.Version });
                    table.CheckConstraint("CK_SettingsUnitProfileVersions_Codes", "LEN(LTRIM(RTRIM([UnitCode]))) > 0 AND LEN(LTRIM(RTRIM([DimensionCode]))) > 0");
                    table.CheckConstraint("CK_SettingsUnitProfileVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [UnitId] <> '00000000-0000-0000-0000-000000000000' AND [ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsUnitProfileVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsUnitProfileVersions_Scale", "[Scale] BETWEEN 0 AND 28 AND ([IsKilogram] = 0 OR [Scale] <= 3)");
                    table.CheckConstraint("CK_SettingsUnitProfileVersions_Source", "[SourceKind] IN ('CompiledBaselineV0','UserMaintained')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnitProfileVersions_CompanyId_ProfileId",
                schema: "settings",
                table: "UnitProfileVersions",
                columns: new[] { "CompanyId", "ProfileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a stored unit profile version or a baseline transfer marker is the company's record of which
            // unit it may use from when and at what scale; dropping it would silently return reads to the catalog.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[UnitProfileVersions])
                    OR EXISTS (SELECT 1 FROM [settings].[UnitProfileBaselineTransfers])
                BEGIN
                    THROW 51341, 'Unit profile versions or a baseline transfer exist; AddUnitProfileVersions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "UnitProfileBaselineTransfers",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "UnitProfileVersions",
                schema: "settings");
        }
    }
}
