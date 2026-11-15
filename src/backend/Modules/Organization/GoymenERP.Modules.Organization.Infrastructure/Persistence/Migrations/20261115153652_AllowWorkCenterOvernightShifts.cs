using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowWorkCenterOvernightShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KIM-S25 (KIM-D33): an end earlier than the start spills into the next day; only start = end stays
            // invalid. Every row the older check admitted satisfies the new one, so Up needs no data guard.
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkCenterCalendarShifts_Window",
                schema: "organization",
                table: "WorkCenterCalendarShifts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkCenterCalendarShifts_Window",
                schema: "organization",
                table: "WorkCenterCalendarShifts",
                sql: "[StartTime] <> [EndTime]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // KIM-S25 (KIM-D33): the older same-day check cannot hold a shift that spills into the next day. Every
            // revision row counts, because shift rows are append-only and the check covers them all. The overnight
            // rows are never rewritten or dropped here (forward fix); an empty store or same-day rows roll back freely.
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [organization].[WorkCenterCalendarShifts] WHERE [EndTime] < [StartTime]) BEGIN THROW 51444, 'Overnight work-centre shifts exist; AllowWorkCenterOvernightShifts is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkCenterCalendarShifts_Window",
                schema: "organization",
                table: "WorkCenterCalendarShifts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkCenterCalendarShifts_Window",
                schema: "organization",
                table: "WorkCenterCalendarShifts",
                sql: "[StartTime] < [EndTime]");
        }
    }
}
