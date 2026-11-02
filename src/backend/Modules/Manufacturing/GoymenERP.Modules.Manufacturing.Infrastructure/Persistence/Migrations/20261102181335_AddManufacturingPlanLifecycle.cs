using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingPlanLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_Approval",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_Enums",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId_PlanId",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                schema: "manufacturing",
                table: "Plans",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                schema: "manufacturing",
                table: "Plans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                schema: "manufacturing",
                table: "Plans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LineSpeedMetresPerMinute",
                schema: "manufacturing",
                table: "Plans",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetUsableWidthMm",
                schema: "manufacturing",
                table: "Plans",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LineSpeedMetresPerMinute",
                schema: "manufacturing",
                table: "PlanRevisions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValueSource",
                schema: "manufacturing",
                table: "PlanParameterSnapshotValues",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlanRejections",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RejectionNo = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PlanVersion = table.Column<long>(type: "bigint", nullable: false),
                    RejectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanRejections", x => new { x.CompanyId, x.PlanId, x.RejectionNo });
                    table.CheckConstraint("CK_PlanRejections_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PlanId] <> '00000000-0000-0000-0000-000000000000' AND [RejectedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [RejectionNo] >= 1 AND [PlanVersion] >= 2");
                    table.CheckConstraint("CK_PlanRejections_Text", "LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_PlanRejections_Plans_CompanyId_PlanId",
                        columns: x => new { x.CompanyId, x.PlanId },
                        principalSchema: "manufacturing",
                        principalTable: "Plans",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_Approval",
                schema: "manufacturing",
                table: "Plans",
                sql: "([Status] = 1 AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Status] = 2 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL) OR ([Status] = 3 AND (([ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_Cancellation",
                schema: "manufacturing",
                table: "Plans",
                sql: "([Status] = 3 AND [CancelledByUserId] IS NOT NULL AND [CancelledByUserId] <> '00000000-0000-0000-0000-000000000000' AND [CancelledAtUtc] IS NOT NULL AND [CancellationReason] IS NOT NULL AND LEN([CancellationReason]) > 0) OR ([Status] <> 3 AND [CancelledByUserId] IS NULL AND [CancelledAtUtc] IS NULL AND [CancellationReason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_Enums",
                schema: "manufacturing",
                table: "Plans",
                sql: "[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_LineSpeed",
                schema: "manufacturing",
                table: "Plans",
                sql: "[LineSpeedMetresPerMinute] IS NULL OR ([LineSpeedMetresPerMinute] > 0 AND [Process] IN (1, 3))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_NetUsableWidth",
                schema: "manufacturing",
                table: "Plans",
                sql: "[NetUsableWidthMm] IS NULL OR ([NetUsableWidthMm] > 0 AND [Process] = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlanRevisions_LineSpeed",
                schema: "manufacturing",
                table: "PlanRevisions",
                sql: "[LineSpeedMetresPerMinute] IS NULL OR [LineSpeedMetresPerMinute] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlanParameterSnapshotValues_Source",
                schema: "manufacturing",
                table: "PlanParameterSnapshotValues",
                sql: "[ValueSource] IS NULL OR ([ValueSource] = 'Plan' AND [NumericValue] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_PlanId",
                schema: "manufacturing",
                table: "Orders",
                columns: new[] { "CompanyId", "PlanId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanRejections_CompanyId_PlanId_PlanVersion",
                schema: "manufacturing",
                table: "PlanRejections",
                columns: new[] { "CompanyId", "PlanId", "PlanVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanRejections",
                schema: "manufacturing");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_Approval",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_Cancellation",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_Enums",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_LineSpeed",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Plans_NetUsableWidth",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlanRevisions_LineSpeed",
                schema: "manufacturing",
                table: "PlanRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlanParameterSnapshotValues_Source",
                schema: "manufacturing",
                table: "PlanParameterSnapshotValues");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId_PlanId",
                schema: "manufacturing",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "LineSpeedMetresPerMinute",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "NetUsableWidthMm",
                schema: "manufacturing",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "LineSpeedMetresPerMinute",
                schema: "manufacturing",
                table: "PlanRevisions");

            migrationBuilder.DropColumn(
                name: "ValueSource",
                schema: "manufacturing",
                table: "PlanParameterSnapshotValues");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_Approval",
                schema: "manufacturing",
                table: "Plans",
                sql: "([Status] = 1 AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Status] = 2 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Plans_Enums",
                schema: "manufacturing",
                table: "Plans",
                sql: "[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId_PlanId",
                schema: "manufacturing",
                table: "Orders",
                columns: new[] { "CompanyId", "PlanId" });
        }
    }
}
