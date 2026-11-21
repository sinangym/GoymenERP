using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginRateLimitTrustedProxies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrustedProxies",
                schema: "settings",
                table: "LoginRateLimitVersions",
                type: "varchar(4000)",
                unicode: false,
                maxLength: 4000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded trusted proxy list is part of the group's audited login attempt limit (KIM-D42); dropping it
            // would silently lose that history. Versions that only hold the empty list roll back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[LoginRateLimitVersions] WHERE [TrustedProxies] <> '')
                BEGIN
                    THROW 51734, 'Login rate limit versions hold a trusted proxy list; AddLoginRateLimitTrustedProxies is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropColumn(
                name: "TrustedProxies",
                schema: "settings",
                table: "LoginRateLimitVersions");
        }
    }
}
