using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftSourcePlanLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftSourcePlanLines",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    LineQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    LockedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    StockQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ProductionQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PurchasingQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SubcontractQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    LinkedSupplyConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    SavedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SavedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftSourcePlanLines", x => new { x.CompanyId, x.DraftId, x.DraftVersion, x.LineId });
                    table.CheckConstraint("CK_DraftSourcePlanLines_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [LineNo] >= 1 AND [DraftVersion] >= 3 AND [LineQuantity] > 0 AND [LockedQuantity] >= 0 AND [StockQuantity] >= 0 AND [ProductionQuantity] >= 0 AND [PurchasingQuantity] >= 0 AND [SubcontractQuantity] >= 0 AND [LockedQuantity] < [LineQuantity] AND [LockedQuantity] + [StockQuantity] + [ProductionQuantity] + [PurchasingQuantity] + [SubcontractQuantity] = [LineQuantity] AND LEN([SavedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_DraftSourcePlanLines_DraftLines_CompanyId_DraftId_LineNo",
                        columns: x => new { x.CompanyId, x.DraftId, x.LineNo },
                        principalSchema: "sales",
                        principalTable: "DraftLines",
                        principalColumns: new[] { "CompanyId", "DraftId", "LineNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftSourcePlanLines_DraftLines_CompanyId_DraftId_LineNo_LineId",
                        columns: x => new { x.CompanyId, x.DraftId, x.LineNo, x.LineId },
                        principalSchema: "sales",
                        principalTable: "DraftLines",
                        principalColumns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftSourcePlanLines_Drafts_CompanyId_DraftId",
                        columns: x => new { x.CompanyId, x.DraftId },
                        principalSchema: "sales",
                        principalTable: "Drafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftSourcePlanLines_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftSourcePlanLines",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftSourcePlanLines_Line",
                schema: "sales",
                table: "DraftSourcePlanLines",
                columns: new[] { "CompanyId", "DraftId", "LineId", "DraftVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded source plan (SAT-D252..SAT-D255) is the order's audited supply decision; dropping the table
            // would erase it. An empty table rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[DraftSourcePlanLines])
                BEGIN
                    THROW 51241, 'Recorded order source plans exist; AddDraftSourcePlanLines is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "DraftSourcePlanLines",
                schema: "sales");
        }
    }
}
