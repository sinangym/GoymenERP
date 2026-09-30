using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// INV-AL-1 change: the append-only history of allocation resizes and moves. Down refuses while any change exists:
    /// the history (and the allocations it re-stated) is never deleted; rollback then requires a reviewed forward fix.
    /// </summary>
    public partial class AddInventoryAllocationChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllocationChanges",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SourceAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceLabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRemainingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TargetAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetLabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetRemainingKg = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationChanges", x => x.Id);
                    table.CheckConstraint("CK_AllocationChanges_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceAllocationId] <> '00000000-0000-0000-0000-000000000000' AND [TargetAllocationId] <> '00000000-0000-0000-0000-000000000000' AND [SourceLabelId] <> '00000000-0000-0000-0000-000000000000' AND [TargetLabelId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AllocationChanges_Kind", "([Kind] = 1 AND [SourceAllocationId] = [TargetAllocationId] AND [SourceLabelId] = [TargetLabelId] AND [TargetRemainingKg] <> [SourceRemainingKg]) OR ([Kind] = 2 AND [SourceAllocationId] <> [TargetAllocationId] AND [SourceLabelId] <> [TargetLabelId])");
                    table.CheckConstraint("CK_AllocationChanges_State", "[SourceVersion] >= 1 AND [SourceRemainingKg] > 0 AND [TargetRemainingKg] > 0");
                    table.CheckConstraint("CK_AllocationChanges_Text", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([ChangedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_AllocationChanges_Allocations_CompanyId_SourceAllocationId",
                        columns: x => new { x.CompanyId, x.SourceAllocationId },
                        principalSchema: "inventory",
                        principalTable: "Allocations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationChanges_Allocations_CompanyId_TargetAllocationId",
                        columns: x => new { x.CompanyId, x.TargetAllocationId },
                        principalSchema: "inventory",
                        principalTable: "Allocations",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationChanges_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationChanges_Labels_CompanyId_SourceLabelId",
                        columns: x => new { x.CompanyId, x.SourceLabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationChanges_Labels_CompanyId_TargetLabelId",
                        columns: x => new { x.CompanyId, x.TargetLabelId },
                        principalSchema: "inventory",
                        principalTable: "Labels",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationChanges_CompanyId_SourceLabelId",
                schema: "inventory",
                table: "AllocationChanges",
                columns: new[] { "CompanyId", "SourceLabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationChanges_CompanyId_TargetLabelId",
                schema: "inventory",
                table: "AllocationChanges",
                columns: new[] { "CompanyId", "TargetLabelId" });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationChanges_Target",
                schema: "inventory",
                table: "AllocationChanges",
                columns: new[] { "CompanyId", "TargetAllocationId" });

            migrationBuilder.CreateIndex(
                name: "UX_AllocationChanges_SourceVersion",
                schema: "inventory",
                table: "AllocationChanges",
                columns: new[] { "CompanyId", "SourceAllocationId", "SourceVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [inventory].[AllocationChanges])
                    THROW 51053, 'Inventory allocation changes exist; use a reviewed forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "AllocationChanges",
                schema: "inventory");
        }
    }
}
