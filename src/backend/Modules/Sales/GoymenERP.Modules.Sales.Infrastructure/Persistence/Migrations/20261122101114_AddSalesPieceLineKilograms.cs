using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPieceLineKilograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualKg",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TheoreticalKg",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DraftLineKilogramsPerUnit",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KilogramsPerUnit = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SourceProof = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftLineKilogramsPerUnit", x => new { x.CompanyId, x.DraftId, x.LineId, x.Revision });
                    table.CheckConstraint("CK_DraftLineKilogramsPerUnit_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [UnitId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND [LineNo] >= 1");
                    table.CheckConstraint("CK_DraftLineKilogramsPerUnit_Proof", "ISJSON([SourceProof]) = 1 AND LEN([SourceProof]) BETWEEN 2 AND 65536");
                    table.CheckConstraint("CK_DraftLineKilogramsPerUnit_Value", "[KilogramsPerUnit] > 0");
                    table.ForeignKey(
                        name: "FK_DraftLineKilogramsPerUnit_DraftLines_CompanyId_DraftId_LineNo_LineId",
                        columns: x => new { x.CompanyId, x.DraftId, x.LineNo, x.LineId },
                        principalSchema: "sales",
                        principalTable: "DraftLines",
                        principalColumns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLines_KilogramInformation",
                schema: "sales",
                table: "InvoiceLines",
                sql: "([TheoreticalKg] IS NULL AND [ActualKg] IS NULL) OR ([TheoreticalKg] IS NOT NULL AND [TheoreticalKg] > 0 AND [ActualKg] IS NOT NULL AND [ActualKg] > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_DraftLineKilogramsPerUnit_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftLineKilogramsPerUnit",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" });

            // SAT-D434: the piece line's kg information is as immutable as the rest of an invoice line.
            migrationBuilder.Sql(SalesPieceLineKilogramGuards.InvoiceLineGuardWithKilogramsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: refuses while a pinned kg/unit or invoice kg information exists (THROW 52051), before any drop.
            migrationBuilder.Sql(SalesPieceLineKilogramGuards.DownGuardSql);
            migrationBuilder.Sql(SalesPieceLineKilogramGuards.InvoiceLineGuardPreviousSql);

            migrationBuilder.DropTable(
                name: "DraftLineKilogramsPerUnit",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLines_KilogramInformation",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "ActualKg",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "TheoreticalKg",
                schema: "sales",
                table: "InvoiceLines");
        }
    }
}
