using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkCenterCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkCenterCalendars",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCenterCalendars", x => x.Id);
                    table.UniqueConstraint("AK_WorkCenterCalendars_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_WorkCenterCalendars_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_WorkCenterCalendars_WorkCenters_CompanyId_WorkCenterId",
                        columns: x => new { x.CompanyId, x.WorkCenterId },
                        principalSchema: "organization",
                        principalTable: "WorkCenters",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkCenterCalendarClosedDates",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCenterCalendarClosedDates", x => x.Id);
                    table.CheckConstraint("CK_WorkCenterCalendarClosedDates_Revision", "[Revision] >= 1");
                    table.ForeignKey(
                        name: "FK_WorkCenterCalendarClosedDates_WorkCenterCalendars_CompanyId_CalendarId",
                        columns: x => new { x.CompanyId, x.CalendarId },
                        principalSchema: "organization",
                        principalTable: "WorkCenterCalendars",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkCenterCalendarShifts",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Day = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCenterCalendarShifts", x => x.Id);
                    table.CheckConstraint("CK_WorkCenterCalendarShifts_Day", "[Day] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_WorkCenterCalendarShifts_Revision", "[Revision] >= 1");
                    table.CheckConstraint("CK_WorkCenterCalendarShifts_Window", "[StartTime] < [EndTime]");
                    table.ForeignKey(
                        name: "FK_WorkCenterCalendarShifts_WorkCenterCalendars_CompanyId_CalendarId",
                        columns: x => new { x.CompanyId, x.CalendarId },
                        principalSchema: "organization",
                        principalTable: "WorkCenterCalendars",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenterCalendarClosedDates_CompanyId_CalendarId_Revision_Date",
                schema: "organization",
                table: "WorkCenterCalendarClosedDates",
                columns: new[] { "CompanyId", "CalendarId", "Revision", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenterCalendars_CompanyId_WorkCenterId",
                schema: "organization",
                table: "WorkCenterCalendars",
                columns: new[] { "CompanyId", "WorkCenterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenterCalendarShifts_CompanyId_CalendarId_Revision_Day_StartTime",
                schema: "organization",
                table: "WorkCenterCalendarShifts",
                columns: new[] { "CompanyId", "CalendarId", "Revision", "Day", "StartTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [organization].[WorkCenterCalendars]) BEGIN THROW 51361, 'Work-centre calendars exist; AddWorkCenterCalendar is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "WorkCenterCalendarClosedDates",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "WorkCenterCalendarShifts",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "WorkCenterCalendars",
                schema: "organization");
        }
    }
}
