using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// WP15 (1.16): the insert-only, versioned legal deadline calendar tables. Nothing is seeded (values wait for the financial
    /// advisor's confirmation). Down refuses with THROW 52280 while any recorded version exists (forward fix); an empty store
    /// rolls back freely.
    /// </summary>
    public partial class AddLegalDeadlineCalendars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegalDeadlineCalendarVersions",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    EntryCount = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDeadlineCalendarVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_SettingsLegalDeadlineCalendarVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] > 0 AND [EntryCount] BETWEEN 0 AND 8 AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "LegalDeadlineCalendarEntries",
                schema: "settings",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Obligation = table.Column<int>(type: "int", nullable: false),
                    PeriodMonths = table.Column<int>(type: "int", nullable: false),
                    MonthsAfterPeriodEnd = table.Column<int>(type: "int", nullable: false),
                    DueDayOfMonth = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDeadlineCalendarEntries", x => new { x.CompanyId, x.Version, x.Obligation });
                    table.CheckConstraint("CK_SettingsLegalDeadlineCalendarEntries_Entry", "[Obligation] BETWEEN 1 AND 8 AND [PeriodMonths] IN (1, 3, 12) AND [MonthsAfterPeriodEnd] BETWEEN 0 AND 12 AND [DueDayOfMonth] BETWEEN 1 AND 31");
                    table.ForeignKey(
                        name: "FK_LegalDeadlineCalendarEntries_LegalDeadlineCalendarVersions_CompanyId_Version",
                        columns: x => new { x.CompanyId, x.Version },
                        principalSchema: "settings",
                        principalTable: "LegalDeadlineCalendarVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [settings].[LegalDeadlineCalendarVersions])
                BEGIN
                    THROW 52280, 'A recorded legal deadline calendar version would be lost; AddLegalDeadlineCalendars is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "LegalDeadlineCalendarEntries",
                schema: "settings");

            migrationBuilder.DropTable(
                name: "LegalDeadlineCalendarVersions",
                schema: "settings");
        }
    }
}
