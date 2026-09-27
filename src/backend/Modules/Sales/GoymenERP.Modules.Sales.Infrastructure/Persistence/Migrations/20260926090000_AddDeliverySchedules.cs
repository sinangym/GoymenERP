using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// DEC-SAL-003 (KILITLI, verbatim: "Termin üst bilgi, satır ve miktar dilimleriyle izlenir; termin güncellemesi
/// planlama/sevkiyat rolündedir ve yeniden onay gerektirmez, ancak auditlidir."): the recorded delivery-schedule
/// changes of a draft. Two NEW append-only tables; no existing table, column or constraint changes. The dates a draft
/// was created with stay in its own rows (schedule 0); each recorded change adds one schedule row and one date row per
/// line (SliceNo 0) and per existing slice (SliceNo >= 1). The write guard admits no column of either table, so a
/// written schedule is never modified or deleted.
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260926090000_AddDeliverySchedules")]
public sealed class AddDeliverySchedules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DraftDeliverySchedules",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScheduleNo = table.Column<int>(type: "int", nullable: false),
                RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                ChangedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftDeliverySchedules", x => new { x.CompanyId, x.DraftId, x.ScheduleNo });
                table.CheckConstraint("CK_DraftDeliverySchedules_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [ScheduleNo] >= 1 AND [DraftVersion] >= 2 AND LEN([ChangedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_DraftDeliverySchedules_Drafts_CompanyId_DraftId",
                    columns: x => new { x.CompanyId, x.DraftId },
                    principalSchema: "sales",
                    principalTable: "Drafts",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DraftDeliveryScheduleDates",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScheduleNo = table.Column<int>(type: "int", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                SliceNo = table.Column<int>(type: "int", nullable: false),
                SliceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RequestedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftDeliveryScheduleDates", x => new { x.CompanyId, x.DraftId, x.ScheduleNo, x.LineNo, x.SliceNo });
                table.CheckConstraint("CK_DraftDeliveryScheduleDates_Position", "[LineNo] >= 1 AND [SliceNo] >= 0");
                table.CheckConstraint("CK_DraftDeliveryScheduleDates_Slice", "([SliceNo] = 0 AND [SliceId] IS NULL) OR ([SliceNo] >= 1 AND [SliceId] IS NOT NULL AND [SliceId] <> '00000000-0000-0000-0000-000000000000' AND [RequestedDeliveryDate] IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_DraftDeliveryScheduleDates_DraftDeliverySchedules_CompanyId_DraftId_ScheduleNo",
                    columns: x => new { x.CompanyId, x.DraftId, x.ScheduleNo },
                    principalSchema: "sales",
                    principalTable: "DraftDeliverySchedules",
                    principalColumns: new[] { "CompanyId", "DraftId", "ScheduleNo" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DraftDeliveryScheduleDates_DraftLines_CompanyId_DraftId_LineNo",
                    columns: x => new { x.CompanyId, x.DraftId, x.LineNo },
                    principalSchema: "sales",
                    principalTable: "DraftLines",
                    principalColumns: new[] { "CompanyId", "DraftId", "LineNo" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DraftDeliveryScheduleDates_CompanyId_DraftId_LineNo",
            schema: "sales",
            table: "DraftDeliveryScheduleDates",
            columns: new[] { "CompanyId", "DraftId", "LineNo" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // DEC-SAL-003 says the update is AUDITED: a recorded schedule is the only place a draft's current delivery dates
        // live after a change (the draft's own rows keep the dates as created), so dropping the tables would silently
        // revert every rescheduled order to its first dates. While any schedule exists the rollback is refused with this
        // module's own error (51054, used by no other module); an empty database rolls back freely. Forward fix, the
        // AddDraftApproval (51045) / AddDraftRevisions (51049) / AddDraftWithdrawal (51050) pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[DraftDeliverySchedules]) OR EXISTS (SELECT 1 FROM [sales].[DraftDeliveryScheduleDates])
                THROW 51054, 'Sales delivery schedules exist; AddDeliverySchedules is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropTable(
            name: "DraftDeliveryScheduleDates",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "DraftDeliverySchedules",
            schema: "sales");
    }
}
