using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryFutureSupplyConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FutureSupplyConversions",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FutureAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PhysicalAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PhysicalQuantityKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    FailureReason = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FutureSupplyConversions", x => x.Id);
                    table.CheckConstraint("CK_FutureSupplyConversions_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [AcceptanceId] <> '00000000-0000-0000-0000-000000000000' AND [FutureAllocationId] <> '00000000-0000-0000-0000-000000000000' AND [LabelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_FutureSupplyConversions_Outcome", "[Quantity] > 0 AND (([Outcome] = 1 AND [PhysicalAllocationId] IS NOT NULL AND [PhysicalAllocationId] <> '00000000-0000-0000-0000-000000000000' AND [PhysicalQuantityKg] IS NOT NULL AND [PhysicalQuantityKg] > 0 AND [FailureReason] IS NULL) OR ([Outcome] = 2 AND [PhysicalAllocationId] IS NULL AND [PhysicalQuantityKg] IS NULL AND [FailureReason] IS NOT NULL AND LEN([FailureReason]) > 0))");
                    table.ForeignKey(
                        name: "FK_FutureSupplyConversions_Allocations_CompanyId_PhysicalAllocationId",
                        columns: x => new { x.CompanyId, x.PhysicalAllocationId },
                        principalSchema: "inventory",
                        principalTable: "Allocations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FutureSupplyConversions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FutureSupplyConversions_FutureSupplyAllocations_CompanyId_FutureAllocationId",
                        columns: x => new { x.CompanyId, x.FutureAllocationId },
                        principalSchema: "inventory",
                        principalTable: "FutureSupplyAllocations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FutureSupplyConversions_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FutureSupplyConversions_CompanyId_LabelId",
                schema: "inventory",
                table: "FutureSupplyConversions",
                columns: new[] { "CompanyId", "LabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_FutureSupplyConversions_FutureAllocation",
                schema: "inventory",
                table: "FutureSupplyConversions",
                columns: new[] { "CompanyId", "FutureAllocationId" });

            migrationBuilder.CreateIndex(
                name: "UX_FutureSupplyConversions_Acceptance",
                schema: "inventory",
                table: "FutureSupplyConversions",
                columns: new[] { "CompanyId", "AcceptanceId", "FutureAllocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FutureSupplyConversions_PhysicalAllocation",
                schema: "inventory",
                table: "FutureSupplyConversions",
                columns: new[] { "CompanyId", "PhysicalAllocationId" },
                unique: true,
                filter: "[PhysicalAllocationId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a conversion link is the only record that ties a consumed future allocation to its physical allocation.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[FutureSupplyConversions])
                BEGIN
                    THROW 51711, 'Future supply conversion links exist; AddInventoryFutureSupplyConversions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "FutureSupplyConversions",
                schema: "inventory");
        }
    }
}
