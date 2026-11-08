using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftLineClosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftLineClosures",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    BasisRevision = table.Column<int>(type: "int", nullable: false),
                    Ordered = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ShippedAtClosure = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CancelledRemainder = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Band = table.Column<int>(type: "int", nullable: false),
                    ClosedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftLineClosures", x => new { x.CompanyId, x.DraftId, x.LineId });
                    table.CheckConstraint("CK_DraftLineClosures_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [LineNo] >= 1 AND [BasisRevision] >= 0 AND [Band] BETWEEN 1 AND 3 AND [DraftVersion] >= 5 AND LEN([Ordered]) > 0 AND LEN([ShippedAtClosure]) > 0 AND LEN([CancelledRemainder]) > 0 AND LEN([ClosedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_DraftLineClosures_DraftLines_CompanyId_DraftId_LineNo",
                        columns: x => new { x.CompanyId, x.DraftId, x.LineNo },
                        principalSchema: "sales",
                        principalTable: "DraftLines",
                        principalColumns: new[] { "CompanyId", "DraftId", "LineNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftLineClosures_Drafts_CompanyId_DraftId",
                        columns: x => new { x.CompanyId, x.DraftId },
                        principalSchema: "sales",
                        principalTable: "Drafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftLineClosures_CompanyId_DraftId_LineNo",
                schema: "sales",
                table: "DraftLineClosures",
                columns: new[] { "CompanyId", "DraftId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "UX_DraftLineClosures_DraftVersion",
                schema: "sales",
                table: "DraftLineClosures",
                columns: new[] { "CompanyId", "DraftId", "DraftVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SAT-D17 / SAT-D168 forward fix: a recorded line closure is closure history (who counted which remainder as cancelled
            // at which shipped total) that the status and the open-order report read and that the line port enforces (a closed
            // line cannot ship); dropping the table would silently reopen closed lines. Refuse (51227, used by no other
            // migration) while any closure exists; an empty table rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[DraftLineClosures])
                BEGIN
                    THROW 51227, 'Line closures exist; AddDraftLineClosures is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "DraftLineClosures",
                schema: "sales");
        }
    }
}
