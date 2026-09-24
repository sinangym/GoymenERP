using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryPhysicalAllocationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Allocations_CompanyId_LabelId",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Quantity",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_State",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.AddColumn<Guid>(
                name: "DemandId",
                schema: "inventory",
                table: "Allocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DemandKind",
                schema: "inventory",
                table: "Allocations",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DemandLineId",
                schema: "inventory",
                table: "Allocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DemandProductId",
                schema: "inventory",
                table: "Allocations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DemandProductVersion",
                schema: "inventory",
                table: "Allocations",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "inventory",
                table: "Allocations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseReason",
                schema: "inventory",
                table: "Allocations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReleasedAtUtc",
                schema: "inventory",
                table: "Allocations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReservedUntilUtc",
                schema: "inventory",
                table: "Allocations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_ActivePosition",
                schema: "inventory",
                table: "Allocations",
                columns: new[] { "CompanyId", "LabelId", "OwnerKind", "OwnerId", "CustodianKind", "CustodianId", "LocationId", "Block", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_Demand",
                schema: "inventory",
                table: "Allocations",
                columns: new[] { "CompanyId", "DemandKind", "DemandId", "DemandLineId", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations",
                sql: "([DemandKind] IS NULL AND [DemandId] IS NULL AND [DemandLineId] IS NULL AND [DemandProductId] IS NULL AND [DemandProductVersion] IS NULL AND [Reason] IS NULL) OR ([DemandKind] = 'sales.order-line' AND [DemandId] <> '00000000-0000-0000-0000-000000000000' AND [DemandLineId] <> '00000000-0000-0000-0000-000000000000' AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' AND LEN([DemandProductVersion]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Quantity",
                schema: "inventory",
                table: "Allocations",
                sql: "[AllocatedKg] > 0 AND [RemainingKg] >= 0 AND [RemainingKg] <= [AllocatedKg] AND (([Status] = 1 AND [RemainingKg] > 0) OR ([Status] IN (2, 3) AND [RemainingKg] = 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Release",
                schema: "inventory",
                table: "Allocations",
                sql: "([Status] = 3 AND [ReleasedAtUtc] IS NOT NULL AND LEN(LTRIM(RTRIM([ReleaseReason]))) > 0) OR ([Status] <> 3 AND [ReleasedAtUtc] IS NULL AND [ReleaseReason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_State",
                schema: "inventory",
                table: "Allocations",
                sql: "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] = 1 AND [Status] IN (1, 2, 3) AND [Version] >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [inventory].[Allocations]
                    WHERE [DemandKind] IS NOT NULL OR [Status] = 3
                )
                    THROW 51052, 'Physical allocation lifecycle data exists; use a reviewed forward fix.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Allocations_ActivePosition",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropIndex(
                name: "IX_Allocations_Demand",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Demand",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Quantity",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_Release",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Allocations_State",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "DemandId",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "DemandKind",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "DemandLineId",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "DemandProductId",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "DemandProductVersion",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "ReleaseReason",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "ReleasedAtUtc",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "ReservedUntilUtc",
                schema: "inventory",
                table: "Allocations");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_CompanyId_LabelId",
                schema: "inventory",
                table: "Allocations",
                columns: new[] { "CompanyId", "LabelId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_Quantity",
                schema: "inventory",
                table: "Allocations",
                sql: "[AllocatedKg] > 0 AND [RemainingKg] >= 0 AND [RemainingKg] <= [AllocatedKg] AND (([Status] = 1 AND [RemainingKg] > 0) OR ([Status] = 2 AND [RemainingKg] = 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Allocations_State",
                schema: "inventory",
                table: "Allocations",
                sql: "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] = 1 AND [Status] IN (1, 2) AND [Version] >= 1");
        }
    }
}
