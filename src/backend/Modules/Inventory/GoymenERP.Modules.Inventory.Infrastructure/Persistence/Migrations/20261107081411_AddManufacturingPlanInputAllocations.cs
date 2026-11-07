using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingPlanInputAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.AddColumn<decimal>(
                name: "ReleasedKg",
                schema: "inventory",
                table: "Allocations",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Allocations_ActivePlanInput",
                schema: "inventory",
                table: "Allocations",
                columns: new[] { "CompanyId", "DemandId", "DemandLineId" },
                unique: true,
                filter: "[Status] = 1 AND [DemandKind] = 'manufacturing.plan-input'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations",
                sql: "([DemandKind] IS NULL AND [DemandId] IS NULL AND [DemandLineId] IS NULL AND [DemandProductId] IS NULL AND [DemandProductVersion] IS NULL AND [Reason] IS NULL AND [ReservedUntilUtc] IS NULL) OR ([DemandKind] IS NOT NULL AND [DemandKind] = 'sales.order-line' AND [DemandId] IS NOT NULL AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] IS NOT NULL AND [DemandLineId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductId] IS NOT NULL AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductVersion] IS NOT NULL AND LEN([DemandProductVersion]) > 0 AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([DemandKind] IS NOT NULL AND [DemandKind] = 'manufacturing.plan-input' AND [DemandId] IS NOT NULL AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] IS NOT NULL AND [DemandLineId] = [LabelId] AND [DemandProductId] IS NOT NULL AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductVersion] IS NOT NULL AND LEN([DemandProductVersion]) > 0 AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND [ReservedUntilUtc] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_ReleasedKg",
                schema: "inventory",
                table: "Allocations",
                sql: "[ReleasedKg] IS NULL OR ([Status] = 3 AND [ReleasedKg] > 0 AND [ReleasedKg] <= [AllocatedKg])");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Rollback restores the Sales-only demand check. SQL Server refuses that check while any
        /// 'manufacturing.plan-input' row exists, so the rollback fails without effect instead of hiding plan
        /// allocations; with plan inputs present the path is forward-fix (B-01, Kalan Modüller plan K0-2).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Allocations_ActivePlanInput",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_ReleasedKg",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "ReleasedKg",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations",
                sql: "([DemandKind] IS NULL AND [DemandId] IS NULL AND [DemandLineId] IS NULL AND [DemandProductId] IS NULL AND [DemandProductVersion] IS NULL AND [Reason] IS NULL AND [ReservedUntilUtc] IS NULL) OR ([DemandKind] IS NOT NULL AND [DemandKind] = 'sales.order-line' AND [DemandId] IS NOT NULL AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] IS NOT NULL AND [DemandLineId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductId] IS NOT NULL AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductVersion] IS NOT NULL AND LEN([DemandProductVersion]) > 0 AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");
        }
    }
}
