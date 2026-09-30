using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;

/// <summary>
/// AT-055: dispatch-note (irsaliye) DRAFTS prepared from a loading instruction's definite shipments, and their lines. Both tables
/// are append-only, tenant-scoped like every Logistics table and bound by company-leading keys (draft → instruction, line →
/// draft). The single-link guard lives in SQL Server: <c>UX_DispatchNoteLines_ShipmentId</c> (company, instruction, shipment) is
/// UNIQUE over non-nullable columns, so a definite shipment is on at most one dispatch note. Status 1 (Draft) is the only state;
/// there is no number, finalisation, cancellation or e-document column (not decided; DEC-INT-001). Additive only. <c>Down</c>
/// refuses (<c>THROW 51056</c>, Logistics's module refusal) while any draft exists — a recorded draft moved the instruction's
/// version and is replayed by the rebuild, so it is never silently dropped (forward fix); empty tables are dropped and nothing
/// that existed before is touched.
/// </summary>
[DbContext(typeof(LogisticsDbContext))]
[Migration("20260930120000_AddDispatchNoteDrafts")]
public sealed class AddDispatchNoteDrafts : Migration
{
    private const string NotEmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DispatchNotes",
            schema: "logistics",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DispatchNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NoteNo = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                SharesBefore = table.Column<int>(type: "int", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DispatchNotes", x => new { x.CompanyId, x.InstructionId, x.DispatchNoteId });
                table.CheckConstraint("CK_DispatchNotes_Identity", "[DispatchNoteId] <> " + NotEmptyGuid + " AND LEN([CreatedBy]) > 0");
                table.CheckConstraint("CK_DispatchNotes_Status", "[Status] IN (1)");
                table.CheckConstraint("CK_DispatchNotes_Order", "[NoteNo] >= 1 AND [SharesBefore] >= 1");
                table.ForeignKey(
                    name: "FK_DispatchNotes_LoadingInstructions_CompanyId_InstructionId",
                    columns: x => new { x.CompanyId, x.InstructionId },
                    principalSchema: "logistics",
                    principalTable: "LoadingInstructions",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DispatchNoteLines",
            schema: "logistics",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InstructionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DispatchNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                ShipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InstructionLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DispatchNoteLines", x => new { x.CompanyId, x.InstructionId, x.DispatchNoteId, x.LineNo });
                table.CheckConstraint("CK_DispatchNoteLines_LineNo", "[LineNo] >= 1");
                table.CheckConstraint("CK_DispatchNoteLines_Identity",
                    "[ShipmentId] <> " + NotEmptyGuid + " AND [InstructionLineId] <> " + NotEmptyGuid + " AND [SourceOrderId] <> " + NotEmptyGuid
                    + " AND [SourceOrderLineId] <> " + NotEmptyGuid + " AND [ProductId] <> " + NotEmptyGuid + " AND [UnitId] <> " + NotEmptyGuid);
                table.CheckConstraint("CK_DispatchNoteLines_Quantity", "[Quantity] > 0");
                table.ForeignKey(
                    name: "FK_DispatchNoteLines_DispatchNotes_CompanyId_InstructionId_DispatchNoteId",
                    columns: x => new { x.CompanyId, x.InstructionId, x.DispatchNoteId },
                    principalSchema: "logistics",
                    principalTable: "DispatchNotes",
                    principalColumns: new[] { "CompanyId", "InstructionId", "DispatchNoteId" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "UX_DispatchNotes_DispatchNoteId",
            schema: "logistics",
            table: "DispatchNotes",
            columns: new[] { "CompanyId", "DispatchNoteId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_DispatchNotes_NoteNo",
            schema: "logistics",
            table: "DispatchNotes",
            columns: new[] { "CompanyId", "InstructionId", "NoteNo" },
            unique: true);

        // AT-055: a definite shipment is on at most one dispatch note (non-nullable columns: no filter).
        migrationBuilder.CreateIndex(
            name: "UX_DispatchNoteLines_ShipmentId",
            schema: "logistics",
            table: "DispatchNoteLines",
            columns: new[] { "CompanyId", "InstructionId", "ShipmentId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DispatchNoteLines_SourceOrder",
            schema: "logistics",
            table: "DispatchNoteLines",
            columns: new[] { "CompanyId", "SourceOrderId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A dispatch-note draft is an append-only fact of the instruction (it moved the instruction's version and is replayed by
        // the rebuild), so dropping the tables would silently erase it. While any draft exists the rollback is refused, before
        // either drop, with Logistics's own module error (51056, the AddVehicleWeighings refusal); an empty database rolls back
        // freely. Forward fix, the Finance 51051 / Sales 51054 pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [logistics].[DispatchNotes]) OR EXISTS (SELECT 1 FROM [logistics].[DispatchNoteLines])
                THROW 51056, 'Logistics dispatch-note drafts exist; AddDispatchNoteDrafts is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropTable(name: "DispatchNoteLines", schema: "logistics");
        migrationBuilder.DropTable(name: "DispatchNotes", schema: "logistics");
    }
}
