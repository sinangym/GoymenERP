using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftRemainderChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftRemainderChanges",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeNo = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    OrderedQuantityText = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    RequestedQuantityText = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LoadedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DeliveredQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftRemainderChanges", x => new { x.CompanyId, x.DraftId, x.ChangeNo });
                    table.CheckConstraint("CK_DraftRemainderChanges_Floor", "[DeliveredQuantity] >= 0 AND [DeliveredQuantity] <= [ShippedQuantity] AND [ShippedQuantity] <= [LoadedQuantity] AND [ShippedQuantity] > 0");
                    table.CheckConstraint("CK_DraftRemainderChanges_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ChangeNo] >= 1 AND [Revision] >= 0 AND [LineNo] >= 1 AND [DraftVersion] >= 5 AND LEN([OrderedQuantityText]) > 0 AND LEN([Reason]) > 0 AND LEN([RequestedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_DraftRemainderChanges_Kind", "([Kind] = 1 AND [RequestedQuantity] IS NULL AND [RequestedQuantityText] IS NULL) OR ([Kind] = 2 AND [RequestedQuantity] > 0 AND [RequestedQuantityText] IS NOT NULL AND LEN([RequestedQuantityText]) > 0)");
                    table.ForeignKey(
                        name: "FK_DraftRemainderChanges_DraftLines_CompanyId_DraftId_LineNo",
                        columns: x => new { x.CompanyId, x.DraftId, x.LineNo },
                        principalSchema: "sales",
                        principalTable: "DraftLines",
                        principalColumns: new[] { "CompanyId", "DraftId", "LineNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftRemainderChanges_Drafts_CompanyId_DraftId",
                        columns: x => new { x.CompanyId, x.DraftId },
                        principalSchema: "sales",
                        principalTable: "Drafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DraftRemainderDecisions",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeNo = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ResultQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    ResultQuantityText = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LoadedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    ShippedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DeliveredQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftRemainderDecisions", x => new { x.CompanyId, x.DraftId, x.ChangeNo });
                    table.CheckConstraint("CK_DraftRemainderDecisions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [ChangeNo] >= 1 AND [DraftVersion] >= 6 AND LEN([DecidedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_DraftRemainderDecisions_Outcome", "([Outcome] = 2 AND [ResultQuantity] > 0 AND [ResultQuantityText] IS NOT NULL AND LEN([ResultQuantityText]) > 0 AND [Reason] IS NULL AND [LoadedQuantity] IS NOT NULL AND [ShippedQuantity] IS NOT NULL AND [DeliveredQuantity] IS NOT NULL AND [DeliveredQuantity] >= 0 AND [DeliveredQuantity] <= [ShippedQuantity] AND [ShippedQuantity] <= [LoadedQuantity] AND [ResultQuantity] >= [LoadedQuantity]) OR ([Outcome] = 3 AND [ResultQuantity] IS NULL AND [ResultQuantityText] IS NULL AND [LoadedQuantity] IS NULL AND [ShippedQuantity] IS NULL AND [DeliveredQuantity] IS NULL AND ([Reason] IS NULL OR LEN([Reason]) > 0))");
                    table.ForeignKey(
                        name: "FK_DraftRemainderDecisions_DraftRemainderChanges_CompanyId_DraftId_ChangeNo",
                        columns: x => new { x.CompanyId, x.DraftId, x.ChangeNo },
                        principalSchema: "sales",
                        principalTable: "DraftRemainderChanges",
                        principalColumns: new[] { "CompanyId", "DraftId", "ChangeNo" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftRemainderChanges_CompanyId_DraftId_LineNo",
                schema: "sales",
                table: "DraftRemainderChanges",
                columns: new[] { "CompanyId", "DraftId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "UX_DraftRemainderChanges_DraftVersion",
                schema: "sales",
                table: "DraftRemainderChanges",
                columns: new[] { "CompanyId", "DraftId", "DraftVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DraftRemainderDecisions_DraftVersion",
                schema: "sales",
                table: "DraftRemainderDecisions",
                columns: new[] { "CompanyId", "DraftId", "DraftVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remainder requests and decisions are immutable commercial history. Refuse a destructive rollback once either
            // table contains a record; an empty installation can still roll back cleanly. 51058 is Sales' next unused code.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[DraftRemainderChanges])
                    OR EXISTS (SELECT 1 FROM [sales].[DraftRemainderDecisions])
                    THROW 51058, 'Sales remainder history exists; AddDraftRemainderChanges is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "DraftRemainderDecisions",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "DraftRemainderChanges",
                schema: "sales");
        }
    }
}
