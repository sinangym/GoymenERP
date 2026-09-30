using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;

/// <summary>
/// AT-055 lifecycle (user decision 2026-09-30): a dispatch note is approved (with the company's gap-free system number), cancelled
/// (never deleted; its shipments are released) and may carry the official e-İrsaliye number. Hand-written:
/// <list type="bullet">
/// <item><c>DispatchNotes</c> gains the step of its creation (<c>CreatedStep</c>, backfilled from <c>NoteNo</c>: before this
/// migration every step was a creation) and three set-once groups — approval + number, cancellation, e-İrsaliye number — each
/// with its own check; <c>[Status] IN (1, 2, 3)</c>.</item>
/// <item><c>DispatchNoteLines</c> gains <c>Released</c> (0 for every stored line), and the single-link key is rebuilt with an
/// EXPLICIT filter <c>[Released] = 0</c>: a shipment is on at most one non-cancelled note, and a cancelled note's shipments can
/// be put on a new one.</item>
/// <item>The system number (company, series, sequence) and the e-İrsaliye number (company, number) are unique when present
/// (explicit filters); <c>DispatchNoteSeries</c> holds each company's last number (the approval's concurrency token).</item>
/// </list>
/// <c>Down</c> refuses (<c>THROW 51056</c>, Logistics's module refusal) while any lifecycle data exists — a series row or any note
/// that is not a plain draft — since dropping it would silently erase approvals, numbers, cancellations and e-İrsaliye numbers and
/// the unfiltered key could not hold re-linked shipments (forward fix). Plain drafts are the previous schema's shape and roll back.
/// </summary>
[DbContext(typeof(LogisticsDbContext))]
[Migration("20260930150000_AddDispatchNoteLifecycle")]
public sealed class AddDispatchNoteLifecycle : Migration
{
    private const string Schema = "logistics";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_DispatchNotes_Status", schema: Schema, table: "DispatchNotes");
        migrationBuilder.DropCheckConstraint(name: "CK_DispatchNotes_Order", schema: Schema, table: "DispatchNotes");
        migrationBuilder.DropIndex(name: "UX_DispatchNoteLines_ShipmentId", schema: Schema, table: "DispatchNoteLines");

        migrationBuilder.AddColumn<int>(name: "CreatedStep", schema: Schema, table: "DispatchNotes", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "SeriesCode", schema: Schema, table: "DispatchNotes", type: "nvarchar(8)", maxLength: 8, nullable: true);
        migrationBuilder.AddColumn<long>(name: "SequenceNo", schema: Schema, table: "DispatchNotes", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ApprovedBy", schema: Schema, table: "DispatchNotes", type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "ApprovedAtUtc", schema: Schema, table: "DispatchNotes", type: "datetimeoffset", nullable: true);
        migrationBuilder.AddColumn<int>(name: "ApprovedStep", schema: Schema, table: "DispatchNotes", type: "int", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CancelledBy", schema: Schema, table: "DispatchNotes", type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "CancelledAtUtc", schema: Schema, table: "DispatchNotes", type: "datetimeoffset", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CancelReason", schema: Schema, table: "DispatchNotes", type: "nvarchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddColumn<int>(name: "CancelledStep", schema: Schema, table: "DispatchNotes", type: "int", nullable: true);
        migrationBuilder.AddColumn<string>(name: "EDispatchNumber", schema: Schema, table: "DispatchNotes", type: "varchar(16)", unicode: false, maxLength: 16,
            nullable: true);
        migrationBuilder.AddColumn<string>(name: "EDispatchNumberSetBy", schema: Schema, table: "DispatchNotes", type: "nvarchar(128)", maxLength: 128,
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "EDispatchNumberSetAtUtc", schema: Schema, table: "DispatchNotes", type: "datetimeoffset",
            nullable: true);
        migrationBuilder.AddColumn<int>(name: "EDispatchNumberStep", schema: Schema, table: "DispatchNotes", type: "int", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "Released", schema: Schema, table: "DispatchNoteLines", type: "bit", nullable: false, defaultValue: false);

        // Before this migration a note's only step was its creation, in NoteNo order: the step is the note's number among the
        // instruction's notes. Dynamic SQL, because the column is new in this same batch.
        migrationBuilder.Sql("EXEC(N'UPDATE [logistics].[DispatchNotes] SET [CreatedStep] = [NoteNo];');");

        migrationBuilder.AddCheckConstraint(name: "CK_DispatchNotes_Status", schema: Schema, table: "DispatchNotes", sql: "[Status] IN (1, 2, 3)");
        migrationBuilder.AddCheckConstraint(name: "CK_DispatchNotes_Order", schema: Schema, table: "DispatchNotes",
            sql: "[NoteNo] >= 1 AND [SharesBefore] >= 1 AND [CreatedStep] >= 1");
        migrationBuilder.AddCheckConstraint(name: "CK_DispatchNotes_Approval", schema: Schema, table: "DispatchNotes",
            sql: "([ApprovedStep] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [SeriesCode] IS NULL AND [SequenceNo] IS NULL "
                + "AND [Status] IN (1, 3)) "
                + "OR ([ApprovedStep] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [SeriesCode] IS NOT NULL "
                + "AND [SequenceNo] IS NOT NULL AND [ApprovedStep] > [CreatedStep] AND LEN([ApprovedBy]) > 0 AND LEN([SeriesCode]) > 0 "
                + "AND [SequenceNo] >= 1 AND [Status] IN (2, 3))");
        migrationBuilder.AddCheckConstraint(name: "CK_DispatchNotes_Cancellation", schema: Schema, table: "DispatchNotes",
            sql: "([CancelledStep] IS NULL AND [CancelledBy] IS NULL AND [CancelledAtUtc] IS NULL AND [CancelReason] IS NULL AND [Status] IN (1, 2)) "
                + "OR ([CancelledStep] IS NOT NULL AND [CancelledBy] IS NOT NULL AND [CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL "
                + "AND [CancelledStep] > [CreatedStep] AND ([ApprovedStep] IS NULL OR [CancelledStep] > [ApprovedStep]) "
                + "AND LEN([CancelledBy]) > 0 AND LEN([CancelReason]) > 0 AND [Status] = 3)");
        migrationBuilder.AddCheckConstraint(name: "CK_DispatchNotes_EDispatchNumber", schema: Schema, table: "DispatchNotes",
            sql: "([EDispatchNumberStep] IS NULL AND [EDispatchNumber] IS NULL AND [EDispatchNumberSetBy] IS NULL AND [EDispatchNumberSetAtUtc] IS NULL) "
                + "OR ([EDispatchNumberStep] IS NOT NULL AND [EDispatchNumber] IS NOT NULL AND [EDispatchNumberSetBy] IS NOT NULL "
                + "AND [EDispatchNumberSetAtUtc] IS NOT NULL AND [ApprovedStep] IS NOT NULL AND [EDispatchNumberStep] > [ApprovedStep] "
                + "AND ([CancelledStep] IS NULL OR [EDispatchNumberStep] < [CancelledStep]) AND LEN([EDispatchNumber]) = 16 "
                + "AND LEN([EDispatchNumberSetBy]) > 0)");

        // AT-055: a definite shipment is on at most one NON-CANCELLED dispatch note — explicit filter over the unreleased lines.
        migrationBuilder.CreateIndex(
            name: "UX_DispatchNoteLines_ShipmentId",
            schema: Schema,
            table: "DispatchNoteLines",
            columns: new[] { "CompanyId", "InstructionId", "ShipmentId" },
            unique: true,
            filter: "[Released] = 0");

        migrationBuilder.CreateIndex(
            name: "UX_DispatchNotes_Number",
            schema: Schema,
            table: "DispatchNotes",
            columns: new[] { "CompanyId", "SeriesCode", "SequenceNo" },
            unique: true,
            filter: "[SeriesCode] IS NOT NULL AND [SequenceNo] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UX_DispatchNotes_EDispatchNumber",
            schema: Schema,
            table: "DispatchNotes",
            columns: new[] { "CompanyId", "EDispatchNumber" },
            unique: true,
            filter: "[EDispatchNumber] IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "DispatchNoteSeries",
            schema: Schema,
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SeriesCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                LastSequenceNo = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DispatchNoteSeries", x => new { x.CompanyId, x.SeriesCode });
                table.CheckConstraint("CK_DispatchNoteSeries_Sequence", "LEN([SeriesCode]) > 0 AND [LastSequenceNo] >= 1");
                table.ForeignKey(
                    name: "FK_DispatchNoteSeries_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // An approval, a system number, a cancellation (with its released lines) or an e-İrsaliye number is a fact the previous
        // schema cannot hold, and a re-linked shipment would break its unfiltered key. While any exists the rollback is refused,
        // before any drop, with Logistics's module error (51056, as AddVehicleWeighings / AddDispatchNoteDrafts); forward fix.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [logistics].[DispatchNoteSeries]) OR EXISTS (SELECT 1 FROM [logistics].[DispatchNotes] WHERE [Status] <> 1)
                OR EXISTS (SELECT 1 FROM [logistics].[DispatchNoteLines] WHERE [Released] = 1)
                THROW 51056, 'Logistics dispatch-note approvals, numbers or cancellations exist; AddDispatchNoteLifecycle is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropTable(name: "DispatchNoteSeries", schema: Schema);
        migrationBuilder.DropIndex(name: "UX_DispatchNotes_EDispatchNumber", schema: Schema, table: "DispatchNotes");
        migrationBuilder.DropIndex(name: "UX_DispatchNotes_Number", schema: Schema, table: "DispatchNotes");
        migrationBuilder.DropIndex(name: "UX_DispatchNoteLines_ShipmentId", schema: Schema, table: "DispatchNoteLines");
        migrationBuilder.DropCheckConstraint(name: "CK_DispatchNotes_EDispatchNumber", schema: Schema, table: "DispatchNotes");
        migrationBuilder.DropCheckConstraint(name: "CK_DispatchNotes_Cancellation", schema: Schema, table: "DispatchNotes");
        migrationBuilder.DropCheckConstraint(name: "CK_DispatchNotes_Approval", schema: Schema, table: "DispatchNotes");
        migrationBuilder.DropCheckConstraint(name: "CK_DispatchNotes_Order", schema: Schema, table: "DispatchNotes");
        migrationBuilder.DropCheckConstraint(name: "CK_DispatchNotes_Status", schema: Schema, table: "DispatchNotes");
        foreach (var column in new[]
                 {
                     "EDispatchNumberStep", "EDispatchNumberSetAtUtc", "EDispatchNumberSetBy", "EDispatchNumber", "CancelledStep", "CancelReason",
                     "CancelledAtUtc", "CancelledBy", "ApprovedStep", "ApprovedAtUtc", "ApprovedBy", "SequenceNo", "SeriesCode", "CreatedStep",
                 })
        {
            migrationBuilder.DropColumn(name: column, schema: Schema, table: "DispatchNotes");
        }

        migrationBuilder.DropColumn(name: "Released", schema: Schema, table: "DispatchNoteLines");
        migrationBuilder.AddCheckConstraint(name: "CK_DispatchNotes_Order", schema: Schema, table: "DispatchNotes",
            sql: "[NoteNo] >= 1 AND [SharesBefore] >= 1");
        migrationBuilder.AddCheckConstraint(name: "CK_DispatchNotes_Status", schema: Schema, table: "DispatchNotes", sql: "[Status] IN (1)");
        migrationBuilder.CreateIndex(
            name: "UX_DispatchNoteLines_ShipmentId",
            schema: Schema,
            table: "DispatchNoteLines",
            columns: new[] { "CompanyId", "InstructionId", "ShipmentId" },
            unique: true);
    }
}
