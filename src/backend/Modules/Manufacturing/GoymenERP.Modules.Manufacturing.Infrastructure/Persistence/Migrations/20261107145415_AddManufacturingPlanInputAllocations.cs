using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturingPlanInputAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanInputAllocations",
                schema: "manufacturing",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentRollId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelVersionAfter = table.Column<long>(type: "bigint", nullable: false),
                    AllocatedKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PlanVersion = table.Column<long>(type: "bigint", nullable: false),
                    AllocatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanInputAllocations", x => new { x.CompanyId, x.PlanId, x.ParentRollId });
                    table.CheckConstraint("CK_PlanInputAllocations_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PlanId] <> '00000000-0000-0000-0000-000000000000' AND [ParentRollId] <> '00000000-0000-0000-0000-000000000000' AND [AllocationId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PlanInputAllocations_Values", "[LabelVersionAfter] >= 1 AND [AllocatedKg] > 0 AND [PlanVersion] >= 2");
                    table.ForeignKey(
                        name: "FK_PlanInputAllocations_Plans_CompanyId_PlanId",
                        columns: x => new { x.CompanyId, x.PlanId },
                        principalSchema: "manufacturing",
                        principalTable: "Plans",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanInputAllocations_CompanyId_AllocationId",
                schema: "manufacturing",
                table: "PlanInputAllocations",
                columns: new[] { "CompanyId", "AllocationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Üretim U3-a (URT-D21, B-01): a row names an allocation Inventory still holds for the plan's input; dropping
            // it would leave the order's input issue unable to name its allocation. Down refuses (THROW 51191) while any
            // row exists (forward fix); an empty table rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [manufacturing].[PlanInputAllocations])
                    THROW 51191, 'A Manufacturing plan input allocation is recorded; forward-fix before downgrade.', 1;
                """);

            migrationBuilder.DropTable(
                name: "PlanInputAllocations",
                schema: "manufacturing");
        }
    }
}
