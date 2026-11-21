using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryNegativeStockCountBoundaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NegativeStockAttemptResolutions_CompanyId_StockCountId",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions");

            migrationBuilder.AddColumn<long>(
                name: "ObservedCountBoundaryVersion",
                schema: "inventory",
                table: "NegativeStockAttempts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CountBoundaryVersion",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NegativeStockCountBoundaries",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    StockCountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NegativeStockCountBoundaries", x => new { x.CompanyId, x.ProductId, x.LocationId, x.Version });
                    table.UniqueConstraint("AK_NegativeStockCountBoundaries_CompanyId_StockCountId_Version", x => new { x.CompanyId, x.StockCountId, x.Version });
                    table.CheckConstraint("CK_InventoryNegativeStockCountBoundaries_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_NegativeStockCountBoundaries_StockCountDecisions_CompanyId_StockCountId",
                        columns: x => new { x.CompanyId, x.StockCountId },
                        principalSchema: "inventory",
                        principalTable: "StockCountDecisions",
                        principalColumns: new[] { "CompanyId", "CountId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NegativeStockCountBoundaries_StockCounts_CompanyId_StockCountId",
                        columns: x => new { x.CompanyId, x.StockCountId },
                        principalSchema: "inventory",
                        principalTable: "StockCounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NegativeStockValidationGuards",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NegativeStockValidationGuards", x => x.CompanyId);
                    table.CheckConstraint("CK_InventoryNegativeStockValidationGuards_Version", "[Version] >= 1");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryNegativeStockAttempts_Boundary",
                schema: "inventory",
                table: "NegativeStockAttempts",
                sql: "[ObservedCountBoundaryVersion] IS NULL OR [ObservedCountBoundaryVersion] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeStockAttemptResolutions_CompanyId_StockCountId_CountBoundaryVersion",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions",
                columns: new[] { "CompanyId", "StockCountId", "CountBoundaryVersion" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryNegativeStockAttemptResolutions_Boundary",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions",
                sql: "[CountBoundaryVersion] IS NULL OR [CountBoundaryVersion] >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeStockCountBoundaries_CompanyId_StockCountId",
                schema: "inventory",
                table: "NegativeStockCountBoundaries",
                columns: new[] { "CompanyId", "StockCountId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_NegativeStockAttemptResolutions_NegativeStockCountBoundaries_CompanyId_StockCountId_CountBoundaryVersion",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions",
                columns: new[] { "CompanyId", "StockCountId", "CountBoundaryVersion" },
                principalSchema: "inventory",
                principalTable: "NegativeStockCountBoundaries",
                principalColumns: new[] { "CompanyId", "StockCountId", "Version" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SAT-D338/D339: an observed boundary and its real approved-count source cannot be discarded.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[NegativeStockCountBoundaries])
                    OR EXISTS (SELECT 1 FROM [inventory].[NegativeStockValidationGuards])
                    OR EXISTS (SELECT 1 FROM [inventory].[NegativeStockAttempts] WHERE [ObservedCountBoundaryVersion] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [inventory].[NegativeStockAttemptResolutions] WHERE [CountBoundaryVersion] IS NOT NULL)
                    THROW 51988, 'Negative stock count-boundary proof is retained; use a reviewed forward fix.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_NegativeStockAttemptResolutions_NegativeStockCountBoundaries_CompanyId_StockCountId_CountBoundaryVersion",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions");

            migrationBuilder.DropTable(
                name: "NegativeStockCountBoundaries",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "NegativeStockValidationGuards",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryNegativeStockAttempts_Boundary",
                schema: "inventory",
                table: "NegativeStockAttempts");

            migrationBuilder.DropIndex(
                name: "IX_NegativeStockAttemptResolutions_CompanyId_StockCountId_CountBoundaryVersion",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryNegativeStockAttemptResolutions_Boundary",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions");

            migrationBuilder.DropColumn(
                name: "ObservedCountBoundaryVersion",
                schema: "inventory",
                table: "NegativeStockAttempts");

            migrationBuilder.DropColumn(
                name: "CountBoundaryVersion",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeStockAttemptResolutions_CompanyId_StockCountId",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions",
                columns: new[] { "CompanyId", "StockCountId" });
        }
    }
}
