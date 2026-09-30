using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the append-only Settings policy and optional exception-approval snapshot for policy-backed order closes.
/// Existing closed orders remain valid without a row. Down refuses while any new decision exists, so evidence is
/// never silently discarded; an empty table rolls back cleanly.
/// </summary>
[DbContext(typeof(ManufacturingDbContext))]
[Migration("20260930220000_AddManufacturingOrderClosureDecision")]
public sealed class AddManufacturingOrderClosureDecision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OrderClosureDecisions",
            schema: "manufacturing",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderVersion = table.Column<long>(type: "bigint", nullable: false),
                PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PolicyVersion = table.Column<long>(type: "bigint", nullable: false),
                Process = table.Column<int>(type: "int", nullable: false),
                MaximumFirePercent = table.Column<string>(type: "varchar(64)", unicode: false,
                    maxLength: 64, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                EffectiveThrough = table.Column<DateOnly>(type: "date", nullable: true),
                SelectedForDate = table.Column<DateOnly>(type: "date", nullable: false),
                ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ApprovalReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                ApprovalOrderVersion = table.Column<long>(type: "bigint", nullable: true),
                ApprovalPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ApprovalPolicyVersion = table.Column<long>(type: "bigint", nullable: true),
                ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderClosureDecisions", x => new { x.CompanyId, x.OrderId });
                table.CheckConstraint("CK_OrderClosureDecisions_Approval",
                    "([ApprovedByUserId] IS NULL AND [ApprovalReason] IS NULL AND [ApprovalOrderVersion] IS NULL "
                    + "AND [ApprovalPolicyId] IS NULL AND [ApprovalPolicyVersion] IS NULL AND [ApprovedAtUtc] IS NULL) "
                    + "OR ([ApprovedByUserId] IS NOT NULL AND [ApprovedByUserId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [ApprovalReason] IS NOT NULL AND LEN([ApprovalReason]) > 0 "
                    + "AND [ApprovalOrderVersion] IS NOT NULL AND [ApprovalOrderVersion] = [OrderVersion] "
                    + "AND [ApprovalPolicyId] IS NOT NULL AND [ApprovalPolicyId] = [PolicyId] "
                    + "AND [ApprovalPolicyVersion] IS NOT NULL AND [ApprovalPolicyVersion] = [PolicyVersion] "
                    + "AND [ApprovedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_OrderClosureDecisions_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [OrderId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [OrderVersion] >= 1 AND [PolicyVersion] >= 1 AND [Process] IN (1, 2, 3, 4, 5) "
                    + "AND LEN([MaximumFirePercent]) > 0 AND [IsActive] = 1");
                table.CheckConstraint("CK_OrderClosureDecisions_Window",
                    "([EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]) "
                    + "AND [SelectedForDate] >= [EffectiveFrom] "
                    + "AND ([EffectiveThrough] IS NULL OR [SelectedForDate] <= [EffectiveThrough])");
                table.ForeignKey(
                    name: "FK_OrderClosureDecisions_Orders_CompanyId_OrderId",
                    columns: x => new { x.CompanyId, x.OrderId },
                    principalSchema: "manufacturing",
                    principalTable: "Orders",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [manufacturing].[OrderClosureDecisions])
                THROW 51048, 'Manufacturing order closure decisions must be forward-fixed before downgrade.', 1;
            """);

        migrationBuilder.DropTable(
            name: "OrderClosureDecisions",
            schema: "manufacturing");
    }
}
