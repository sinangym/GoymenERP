using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMarginCostSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftMarginCosts",
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
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CostPerUnit = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    UnavailableReason = table.Column<int>(type: "int", nullable: true),
                    SourceProof = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftMarginCosts", x => new { x.CompanyId, x.DraftId, x.LineId, x.Revision });
                    table.CheckConstraint("CK_DraftMarginCosts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [UnitId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND [LineNo] >= 1 AND [Quantity] > 0");
                    table.CheckConstraint("CK_DraftMarginCosts_Proof", "ISJSON([SourceProof]) = 1 AND LEN([SourceProof]) BETWEEN 2 AND 65536");
                    table.CheckConstraint("CK_DraftMarginCosts_Value", "([UnavailableReason] IS NULL AND [CurrencyId] IS NOT NULL AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [CostPerUnit] IS NOT NULL AND [CostPerUnit] >= 0) OR ([UnavailableReason] IS NOT NULL AND [UnavailableReason] IN (1,2,3,5,6,7,8,9,10,11,12,13,14) AND [CurrencyId] IS NULL AND [CostPerUnit] IS NULL)");
                    table.ForeignKey(
                        name: "FK_DraftMarginCosts_DraftLines_CompanyId_DraftId_LineNo_LineId",
                        columns: x => new { x.CompanyId, x.DraftId, x.LineNo, x.LineId },
                        principalSchema: "sales",
                        principalTable: "DraftLines",
                        principalColumns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftMarginCosts_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftMarginCosts",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[DraftMarginCosts])
                BEGIN
                    THROW 51993, 'Recorded approval margin cost evidence exists; AddSalesMarginCostSnapshots is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "DraftMarginCosts",
                schema: "sales");
        }
    }
}
