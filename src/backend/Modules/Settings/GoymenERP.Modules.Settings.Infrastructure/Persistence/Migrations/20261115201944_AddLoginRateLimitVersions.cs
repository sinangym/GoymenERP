using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginRateLimitVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoginRateLimitVersions",
                schema: "settings",
                columns: table => new
                {
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitLimit = table.Column<int>(type: "int", nullable: false),
                    WindowSeconds = table.Column<int>(type: "int", nullable: false),
                    PartitionBy = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedInCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginRateLimitVersions", x => x.Version);
                    table.CheckConstraint("CK_SettingsLoginRateLimitVersions_Bounds", "[PermitLimit] BETWEEN 1 AND 1000000 AND [WindowSeconds] BETWEEN 1 AND 86400");
                    table.CheckConstraint("CK_SettingsLoginRateLimitVersions_Identity", "[ProfileId] <> '00000000-0000-0000-0000-000000000000' AND [RecordedInCompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsLoginRateLimitVersions_Partition", "[PartitionBy] IN ('IpAddress','IpAddressAndUserName')");
                    table.CheckConstraint("CK_SettingsLoginRateLimitVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginRateLimitVersions_ProfileId",
                schema: "settings",
                table: "LoginRateLimitVersions",
                column: "ProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: once recorded, the limit is the group's audited login attempt limit (KIM-D34); dropping it would
            // silently fall back to the configured value and lose its history. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[LoginRateLimitVersions])
                BEGIN
                    THROW 51331, 'Login rate limit versions exist; AddLoginRateLimitVersions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "LoginRateLimitVersions",
                schema: "settings");
        }
    }
}
