using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingOrderCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PlanInputAllocations",
                schema: "manufacturing",
                table: "PlanInputAllocations");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId_PlanId",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Close",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Enums",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "manufacturing",
                table: "Orders",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                schema: "manufacturing",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                schema: "manufacturing",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PlanVersionAfterCancel",
                schema: "manufacturing",
                table: "Orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PlanInputAllocations",
                schema: "manufacturing",
                table: "PlanInputAllocations",
                columns: new[] { "CompanyId", "PlanId", "PlanVersion", "ParentRollId" });

            migrationBuilder.CreateTable(
                name: "PlanReapprovals",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReapprovalNo = table.Column<int>(type: "int", nullable: false),
                    PlanVersion = table.Column<long>(type: "bigint", nullable: false),
                    ReapprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReapprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanReapprovals", x => new { x.CompanyId, x.PlanId, x.ReapprovalNo });
                    table.CheckConstraint("CK_PlanReapprovals_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PlanId] <> '00000000-0000-0000-0000-000000000000' AND [ReapprovedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ReapprovalNo] >= 1 AND [PlanVersion] >= 3");
                    table.CheckConstraint("CK_PlanReapprovals_Text", "LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_PlanReapprovals_Plans_CompanyId_PlanId",
                        columns: x => new { x.CompanyId, x.PlanId },
                        principalSchema: "manufacturing",
                        principalTable: "Plans",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_PlanId",
                schema: "manufacturing",
                table: "Orders",
                columns: new[] { "CompanyId", "PlanId" },
                unique: true,
                filter: "[Status] <> 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Cancel",
                schema: "manufacturing",
                table: "Orders",
                sql: "([Status] = 3 AND [CancelledByUserId] IS NOT NULL AND [CancelledByUserId] <> '00000000-0000-0000-0000-000000000000' AND [CancelledAtUtc] IS NOT NULL AND [CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0 AND [PlanVersionAfterCancel] IS NOT NULL AND [PlanVersionAfterCancel] >= 3) OR ([Status] <> 3 AND [CancelledByUserId] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL AND [PlanVersionAfterCancel] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Close",
                schema: "manufacturing",
                table: "Orders",
                sql: "([Status] = 1 AND [ClosedByUserId] IS NULL AND [ClosedAtUtc] IS NULL) OR ([Status] = 2 AND [ClosedByUserId] IS NOT NULL AND [ClosedAtUtc] IS NOT NULL) OR ([Status] = 3 AND [ClosedByUserId] IS NULL AND [ClosedAtUtc] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Enums",
                schema: "manufacturing",
                table: "Orders",
                sql: "[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_PlanReapprovals_CompanyId_PlanId_PlanVersion",
                schema: "manufacturing",
                table: "PlanReapprovals",
                columns: new[] { "CompanyId", "PlanId", "PlanVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a cancelled order, a re-approval or a second allocation generation of a plan would lose its
            // meaning (or break the old allocation key) once these columns and the table are dropped.
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [manufacturing].[Orders] WHERE [Status] = 3) OR EXISTS (SELECT 1 FROM [manufacturing].[PlanReapprovals]) OR EXISTS (SELECT 1 FROM [manufacturing].[PlanInputAllocations] GROUP BY [CompanyId], [PlanId] HAVING COUNT(DISTINCT [PlanVersion]) > 1) BEGIN THROW 51870, 'Cancelled orders, plan re-approvals or re-approved plan allocations exist; AddManufacturingOrderCancellation is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "PlanReapprovals",
                schema: "manufacturing");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PlanInputAllocations",
                schema: "manufacturing",
                table: "PlanInputAllocations");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId_PlanId",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Cancel",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Close",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Enums",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PlanVersionAfterCancel",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PlanInputAllocations",
                schema: "manufacturing",
                table: "PlanInputAllocations",
                columns: new[] { "CompanyId", "PlanId", "ParentRollId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_PlanId",
                schema: "manufacturing",
                table: "Orders",
                columns: new[] { "CompanyId", "PlanId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Close",
                schema: "manufacturing",
                table: "Orders",
                sql: "([Status] = 1 AND [ClosedByUserId] IS NULL AND [ClosedAtUtc] IS NULL) OR ([Status] = 2 AND [ClosedByUserId] IS NOT NULL AND [ClosedAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Enums",
                schema: "manufacturing",
                table: "Orders",
                sql: "[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2)");
        }
    }
}
