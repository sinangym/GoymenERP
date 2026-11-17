using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryNegativeStockAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NegativeStockAttempts",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PermissionCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceChannel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RefusedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MovementKind = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    AvailableKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    MissingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NegativeStockAttempts", x => x.Id);
                    table.UniqueConstraint("AK_NegativeStockAttempts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_InventoryNegativeStockAttempts_State", "[RequestedKg] > 0 AND [AvailableKg] < [RequestedKg] AND [MissingKg] = [RequestedKg] - [AvailableKg] AND [LineNumber] >= 1");
                });

            migrationBuilder.CreateTable(
                name: "NegativeStockAttemptResolutions",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockCountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NegativeStockAttemptResolutions", x => new { x.CompanyId, x.AttemptId });
                    table.ForeignKey(
                        name: "FK_NegativeStockAttemptResolutions_NegativeStockAttempts_CompanyId_AttemptId",
                        columns: x => new { x.CompanyId, x.AttemptId },
                        principalSchema: "inventory",
                        principalTable: "NegativeStockAttempts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NegativeStockAttemptResolutions_StockCounts_CompanyId_StockCountId",
                        columns: x => new { x.CompanyId, x.StockCountId },
                        principalSchema: "inventory",
                        principalTable: "StockCounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NegativeStockAttemptResolutions_CompanyId_StockCountId",
                schema: "inventory",
                table: "NegativeStockAttemptResolutions",
                columns: new[] { "CompanyId", "StockCountId" });

            migrationBuilder.CreateIndex(
                name: "IX_NegativeStockAttempts_CompanyId_MovementId_ActorId_KeyHash",
                schema: "inventory",
                table: "NegativeStockAttempts",
                columns: new[] { "CompanyId", "MovementId", "ActorId", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NegativeStockAttempts_CompanyId_ProductId_LocationId_Id",
                schema: "inventory",
                table: "NegativeStockAttempts",
                columns: new[] { "CompanyId", "ProductId", "LocationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_NegativeStockAttempts_CompanyId_RefusedAtUtc_Id",
                schema: "inventory",
                table: "NegativeStockAttempts",
                columns: new[] { "CompanyId", "RefusedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SAT-D338: refused attempts and their count-resolution history are never discarded by downgrade.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[NegativeStockAttempts])
                    OR EXISTS (SELECT 1 FROM [inventory].[NegativeStockAttemptResolutions])
                    THROW 51986, 'Negative stock attempt history is append-only; use a reviewed forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "NegativeStockAttemptResolutions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "NegativeStockAttempts",
                schema: "inventory");
        }
    }
}
