using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;

/// <summary>
/// INV-OPEN-1 (T07 opening stock, decision A #66 5738113623 §2): the opening count document and its
/// counted pieces. A count is at one of its company's locations; each line names a label of the company
/// and its one receipt movement by (company, line id = movement id), so no counted line exists without its
/// posted receipt. No row is written here: an opening comes from the user's own physical count.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260919120000_AddOpeningCounts")]
public sealed class AddOpeningCounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OpeningCounts",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CountedOn = table.Column<DateOnly>(type: "date", nullable: false),
                SheetReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OpeningCounts", x => x.Id);
                table.UniqueConstraint("AK_OpeningCounts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_OpeningCounts_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [LocationId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_OpeningCounts_Text",
                    "LEN([SheetReference]) > 0 AND LEN([ContentHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_OpeningCounts_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OpeningCounts_Locations_CompanyId_LocationId",
                    columns: x => new { x.CompanyId, x.LocationId },
                    principalSchema: "organization",
                    principalTable: "Locations",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "OpeningCountLines",
            schema: "inventory",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineNumber = table.Column<int>(type: "int", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OpeningCountLines", x => new { x.CompanyId, x.CountId, x.LineNumber });
                table.CheckConstraint("CK_OpeningCountLines_LineNumber", "[LineNumber] >= 1");
                table.CheckConstraint("CK_OpeningCountLines_Quantity", "[QuantityKg] > 0");
                table.ForeignKey(
                    name: "FK_OpeningCountLines_Labels_CompanyId_LabelId",
                    columns: x => new { x.CompanyId, x.LabelId },
                    principalSchema: "inventory",
                    principalTable: "Labels",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OpeningCountLines_Movements_CompanyId_LineId",
                    columns: x => new { x.CompanyId, x.LineId },
                    principalSchema: "inventory",
                    principalTable: "Movements",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OpeningCountLines_OpeningCounts_CompanyId_CountId",
                    columns: x => new { x.CompanyId, x.CountId },
                    principalSchema: "inventory",
                    principalTable: "OpeningCounts",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OpeningCounts_CompanyId_LocationId",
            schema: "inventory",
            table: "OpeningCounts",
            columns: new[] { "CompanyId", "LocationId" });

        migrationBuilder.CreateIndex(
            name: "IX_OpeningCountLines_CompanyId_LabelId",
            schema: "inventory",
            table: "OpeningCountLines",
            columns: new[] { "CompanyId", "LabelId" });

        migrationBuilder.CreateIndex(
            name: "IX_OpeningCountLines_CompanyId_LineId",
            schema: "inventory",
            table: "OpeningCountLines",
            columns: new[] { "CompanyId", "LineId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OpeningCountLines",
            schema: "inventory");

        migrationBuilder.DropTable(
            name: "OpeningCounts",
            schema: "inventory");
    }
}
