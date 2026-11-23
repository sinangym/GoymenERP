using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSegregationOfDutiesPolicyVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SegregationOfDutiesPolicyVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PreparerMayApprove = table.Column<bool>(type: "bit", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegregationOfDutiesPolicyVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_SettingsSegregationOfDutiesPolicyVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0");
                    table.CheckConstraint("CK_SettingsSegregationOfDutiesPolicyVersions_Recorded", "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded version is the audited history of who relaxed or restored segregation of duties and why
            // (NS-01); dropping it would erase that history and silently return a relaxed company to the default. An empty
            // store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[SegregationOfDutiesPolicyVersions])
                BEGIN
                    THROW 51622, 'Segregation of duties versions exist; AddSegregationOfDutiesPolicyVersions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "SegregationOfDutiesPolicyVersions",
                schema: "settings");
        }
    }
}
