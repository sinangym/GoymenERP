using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseTransferScalePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WarehouseTransferScaleVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequireExitWeighing = table.Column<bool>(type: "bit", nullable: false),
                    LocationVersion = table.Column<long>(type: "bigint", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseTransferScaleVersions", x => new { x.CompanyId, x.LocationId, x.Version });
                    table.CheckConstraint("CK_WarehouseTransferScaleVersions_Audit", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.CheckConstraint("CK_WarehouseTransferScaleVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' AND [RecordId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [LocationVersion] > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseTransferScaleVersions_CompanyId_RecordId",
                schema: "settings",
                table: "WarehouseTransferScaleVersions",
                columns: new[] { "CompanyId", "RecordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[WarehouseTransferScaleVersions])
                BEGIN
                    THROW 51990, 'Warehouse transfer scale history exists; AddWarehouseTransferScalePolicies is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "WarehouseTransferScaleVersions",
                schema: "settings");
        }
    }
}
