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
        /// Rollback restores the Sales-only demand check. A guard (THROW 51211) refuses while any
        /// 'manufacturing.plan-input' row exists, so the rollback fails without effect instead of hiding plan
        /// allocations; with plan inputs present the path is forward-fix (B-01, Kalan Modüller plan K0-2).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse before any schema change while a plan input or a recorded released kg exists: the Sales-only
            // demand check cannot hold them, and dropping ReleasedKg would erase the consumed/released split.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[Allocations]
                           WHERE [DemandKind] = N'manufacturing.plan-input' OR [ReleasedKg] IS NOT NULL)
                    THROW 51211, 'Manufacturing plan-input allocation data exists; use a reviewed forward fix.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "UX_Allocations_ActivePlanInput",
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

            // Restore the Sales-only demand check with the trust and enabled state the widened check had: every row
            // the guard admits already satisfies it, and an older migration's own guard must still be the one that
            // refuses rows a reviewed test planted with the check disabled.
            migrationBuilder.Sql(
                """
                DECLARE @trusted bit = 0, @enabled bit = 0;
                SELECT @trusted = CASE WHEN [is_not_trusted] = 0 THEN 1 ELSE 0 END,
                       @enabled = CASE WHEN [is_disabled] = 0 THEN 1 ELSE 0 END
                FROM sys.check_constraints
                WHERE [parent_object_id] = OBJECT_ID(N'[inventory].[Allocations]') AND [name] = N'CK_Allocations_Demand';
                ALTER TABLE [inventory].[Allocations] DROP CONSTRAINT [CK_Allocations_Demand];
                ALTER TABLE [inventory].[Allocations] WITH NOCHECK ADD CONSTRAINT [CK_Allocations_Demand]
                    CHECK (([DemandKind] IS NULL AND [DemandId] IS NULL AND [DemandLineId] IS NULL AND [DemandProductId] IS NULL AND [DemandProductVersion] IS NULL AND [Reason] IS NULL AND [ReservedUntilUtc] IS NULL) OR ([DemandKind] IS NOT NULL AND [DemandKind] = 'sales.order-line' AND [DemandId] IS NOT NULL AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] IS NOT NULL AND [DemandLineId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductId] IS NOT NULL AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductVersion] IS NOT NULL AND LEN([DemandProductVersion]) > 0 AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0));
                IF @enabled = 0
                    ALTER TABLE [inventory].[Allocations] NOCHECK CONSTRAINT [CK_Allocations_Demand];
                ELSE IF @trusted = 1
                    ALTER TABLE [inventory].[Allocations] WITH CHECK CHECK CONSTRAINT [CK_Allocations_Demand];
                """);
        }
    }
}
