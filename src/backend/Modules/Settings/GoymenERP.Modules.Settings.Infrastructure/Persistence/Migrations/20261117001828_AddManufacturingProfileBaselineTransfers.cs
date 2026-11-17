using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingProfileBaselineTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManufacturingProfileBaselineTransfers",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SnapshotHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TransferredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingProfileBaselineTransfers", x => new { x.CompanyId, x.Kind });
                    table.CheckConstraint("CK_SettingsMfgBaseline_Actor", "LEN(LTRIM(RTRIM([ActorId]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_SettingsMfgBaseline_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TransferId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_SettingsMfgBaseline_Kind", "[Kind] IN ('parameters','tolerances') AND [Version] = 1");
                    table.CheckConstraint("CK_SettingsMfgBaseline_Snapshot", "ISJSON([SnapshotJson]) = 1 AND LEN([SnapshotHash]) = 64 AND [SnapshotHash] NOT LIKE '%[^0-9A-F]%'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingProfileBaselineTransfers_CompanyId_TransferId",
                schema: "settings",
                table: "ManufacturingProfileBaselineTransfers",
                columns: new[] { "CompanyId", "TransferId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[ManufacturingProfileBaselineTransfers])
                BEGIN
                    THROW 51966, 'Manufacturing baseline source history exists; AddManufacturingProfileBaselineTransfers is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "ManufacturingProfileBaselineTransfers",
                schema: "settings");
        }
    }
}
