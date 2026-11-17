using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventorySubcontractDeliverySources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.CreateTable(
                name: "SubcontractAllocationSources",
                schema: "inventory",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderVersion = table.Column<long>(type: "bigint", nullable: false),
                    SalesLineRevision = table.Column<int>(type: "int", nullable: false),
                    ManufacturingOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManufacturingOrderVersion = table.Column<long>(type: "bigint", nullable: false),
                    ManufacturingPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManufacturingPlanVersion = table.Column<long>(type: "bigint", nullable: false),
                    OutputFactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputNetKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    SourceHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcontractAllocationSources", x => new { x.CompanyId, x.AllocationId });
                    table.CheckConstraint("CK_SubcontractAllocationSources_Proof", "[CustomerPartyId] <> '00000000-0000-0000-0000-000000000000' AND [CustomerAccountId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderVersion] > 0 AND [SalesLineRevision] >= 0 AND [ManufacturingOrderVersion] > 0 AND [ManufacturingPlanVersion] > 0 AND [ManufacturingOrderId] <> '00000000-0000-0000-0000-000000000000' AND [ManufacturingPlanId] <> '00000000-0000-0000-0000-000000000000' AND [OutputFactId] <> '00000000-0000-0000-0000-000000000000' AND [OutputNetKg] > 0 AND ISJSON([SourceJson])=1 AND LEN([SourceHash])=64");
                    table.ForeignKey(
                        name: "FK_SubcontractAllocationSources_Allocations_CompanyId_AllocationId",
                        columns: x => new { x.CompanyId, x.AllocationId },
                        principalSchema: "inventory",
                        principalTable: "Allocations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubcontractAllocationSources_Labels_CompanyId_LabelId",
                        columns: x => new { x.CompanyId, x.LabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations",
                sql: "([DemandKind] IS NULL AND [DemandId] IS NULL AND [DemandLineId] IS NULL AND [DemandProductId] IS NULL AND [DemandProductVersion] IS NULL AND [Reason] IS NULL AND [ReservedUntilUtc] IS NULL) OR ([DemandKind] IS NOT NULL AND [DemandKind] IN ('sales.order-line','sales.subcontract-service-order-line') AND [DemandId] IS NOT NULL AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] IS NOT NULL AND [DemandLineId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductId] IS NOT NULL AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductVersion] IS NOT NULL AND LEN([DemandProductVersion]) > 0 AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([DemandKind] IS NOT NULL AND [DemandKind] = 'manufacturing.plan-input' AND [DemandId] IS NOT NULL AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] IS NOT NULL AND [DemandLineId] = [LabelId] AND [DemandProductId] IS NOT NULL AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductVersion] IS NOT NULL AND LEN([DemandProductVersion]) > 0 AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND [ReservedUntilUtc] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SubcontractAllocationSources_CompanyId_LabelId",
                schema: "inventory",
                table: "SubcontractAllocationSources",
                columns: new[] { "CompanyId", "LabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubcontractAllocationSources_CompanyId_SalesOrderId_SalesOrderLineId_LabelId",
                schema: "inventory",
                table: "SubcontractAllocationSources",
                columns: new[] { "CompanyId", "SalesOrderId", "SalesOrderLineId", "LabelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[SubcontractAllocationSources])
                    OR EXISTS (SELECT 1 FROM [inventory].[Allocations]
                        WHERE [DemandKind] = N'sales.subcontract-service-order-line')
                BEGIN
                    THROW 51934, 'Customer-owned subcontract allocation evidence exists; AddInventorySubcontractDeliverySources is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "SubcontractAllocationSources",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations",
                sql: "([DemandKind] IS NULL AND [DemandId] IS NULL AND [DemandLineId] IS NULL AND [DemandProductId] IS NULL AND [DemandProductVersion] IS NULL AND [Reason] IS NULL AND [ReservedUntilUtc] IS NULL) OR ([DemandKind] IS NOT NULL AND [DemandKind] = 'sales.order-line' AND [DemandId] IS NOT NULL AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] IS NOT NULL AND [DemandLineId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductId] IS NOT NULL AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductVersion] IS NOT NULL AND LEN([DemandProductVersion]) > 0 AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([DemandKind] IS NOT NULL AND [DemandKind] = 'manufacturing.plan-input' AND [DemandId] IS NOT NULL AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] IS NOT NULL AND [DemandLineId] = [LabelId] AND [DemandProductId] IS NOT NULL AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductVersion] IS NOT NULL AND LEN([DemandProductVersion]) > 0 AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND [ReservedUntilUtc] IS NULL)");
        }
    }
}
