using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// SAT-D262..D267 (SINAN_KARARLARI_2026-11-10_SATIS_EK52): the moving-average purchase cost ledger, one row per
    /// CompanyId + location + product. Down refuses while any ledger row exists: the ledger is never deleted; rollback
    /// then requires a reviewed forward fix.
    /// </summary>
    public partial class AddInventoryAverageCostLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AverageCostLedger",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UnreadableReason = table.Column<int>(type: "int", nullable: true),
                    QuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    AverageCostPerKg = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AverageCostLedger", x => x.Id);
                    table.CheckConstraint("CK_AverageCostLedger_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [LocationId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AverageCostLedger_State", "[Version] >= 1 AND (([Status] = 1 AND [UnreadableReason] IS NULL AND [QuantityKg] IS NOT NULL AND [QuantityKg] >= 0 AND [AverageCostPerKg] IS NOT NULL AND [AverageCostPerKg] >= 0) OR ([Status] = 2 AND [UnreadableReason] IS NOT NULL AND [UnreadableReason] BETWEEN 1 AND 8 AND [QuantityKg] IS NULL AND [AverageCostPerKg] IS NULL))");
                    table.ForeignKey(
                        name: "FK_AverageCostLedger_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AverageCostLedger_Locations_CompanyId_LocationId",
                        columns: x => new { x.CompanyId, x.LocationId },
                        principalSchema: "organization",
                        principalTable: "Locations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_AverageCostLedger_Position",
                schema: "inventory",
                table: "AverageCostLedger",
                columns: new[] { "CompanyId", "LocationId", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[AverageCostLedger])
                    THROW 51250, 'Inventory average cost ledger rows exist; use a reviewed forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "AverageCostLedger",
                schema: "inventory");
        }
    }
}
