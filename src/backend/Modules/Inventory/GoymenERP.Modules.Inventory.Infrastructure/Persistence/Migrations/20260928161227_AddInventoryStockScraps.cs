using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ST-02 scrap write-off: the append-only stock scrap and its one decision, and the <c>Scrap</c> movement kind (7)
    /// an approved scrap posts. Down refuses while a scrap, a decision or a scrap movement exists: history is never
    /// deleted, rollback requires a reviewed forward fix.
    /// </summary>
    public partial class AddInventoryStockScraps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements");

            migrationBuilder.CreateTable(
                name: "StockScraps",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelVersion = table.Column<long>(type: "bigint", nullable: false),
                    OwnerKind = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustodianKind = table.Column<int>(type: "int", nullable: false),
                    CustodianId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Block = table.Column<int>(type: "int", nullable: false),
                    BookKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ScrapKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ScrappedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockScraps", x => x.Id);
                    table.UniqueConstraint("AK_StockScraps_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_StockScraps_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OwnerId] <> '00000000-0000-0000-0000-000000000000' AND [CustodianId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_StockScraps_State", "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2) AND [LabelVersion] >= 1 AND [BookKg] > 0 AND [ScrapKg] > 0 AND [ScrapKg] <= [BookKg]");
                    table.CheckConstraint("CK_StockScraps_Text", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_StockScraps_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockScraps_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockScraps_Locations_CompanyId_LocationId",
                        columns: x => new { x.CompanyId, x.LocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockScrapDecisions",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScrapId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockScrapDecisions", x => new { x.CompanyId, x.ScrapId });
                    table.CheckConstraint("CK_StockScrapDecisions_Decision", "([Decision] = 1 AND [MovementId] IS NOT NULL AND [MovementId] <> '00000000-0000-0000-0000-000000000000') OR ([Decision] = 2 AND [MovementId] IS NULL)");
                    table.CheckConstraint("CK_StockScrapDecisions_Text", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([DecidedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_StockScrapDecisions_Movements_CompanyId_MovementId",
                        columns: x => new { x.CompanyId, x.MovementId },
                        principalSchema: "inventory",
                        principalTable: "Movements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockScrapDecisions_StockScraps_CompanyId_ScrapId",
                        columns: x => new { x.CompanyId, x.ScrapId },
                        principalSchema: "inventory",
                        principalTable: "StockScraps",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 7");

            migrationBuilder.CreateIndex(
                name: "IX_StockScrapDecisions_CompanyId_MovementId",
                schema: "inventory",
                table: "StockScrapDecisions",
                columns: new[] { "CompanyId", "MovementId" },
                unique: true,
                filter: "[MovementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockScraps_CompanyId_LabelId_RecordedAtUtc_Id",
                schema: "inventory",
                table: "StockScraps",
                columns: new[] { "CompanyId", "LabelId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StockScraps_CompanyId_LocationId",
                schema: "inventory",
                table: "StockScraps",
                columns: new[] { "CompanyId", "LocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[StockScraps])
                   OR EXISTS (SELECT 1 FROM [inventory].[StockScrapDecisions])
                   OR EXISTS (SELECT 1 FROM [inventory].[Movements] WHERE [Kind] = 7)
                    THROW 51053, 'Inventory stock scraps or scrap movements exist; use a reviewed forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "StockScrapDecisions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockScraps",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movements_Kind",
                schema: "inventory",
                table: "Movements",
                sql: "[Kind] BETWEEN 1 AND 6");
        }
    }
}
