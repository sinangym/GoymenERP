using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftOverDeliveryApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DraftOverDeliveryApprovals",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalNo = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    BasisRevision = table.Column<int>(type: "int", nullable: false),
                    Ordered = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ApprovedShipped = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftOverDeliveryApprovals", x => new { x.CompanyId, x.DraftId, x.LineId, x.ApprovalNo });
                    table.CheckConstraint("CK_DraftOverDeliveryApprovals_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [LineNo] >= 1 AND [ApprovalNo] >= 1 AND [BasisRevision] >= 0 AND [DraftVersion] >= 5 AND LEN([Ordered]) > 0 AND LEN([ApprovedShipped]) > 0 AND LEN([ApprovedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_DraftOverDeliveryApprovals_DraftLines_CompanyId_DraftId_LineNo",
                        columns: x => new { x.CompanyId, x.DraftId, x.LineNo },
                        principalSchema: "sales",
                        principalTable: "DraftLines",
                        principalColumns: new[] { "CompanyId", "DraftId", "LineNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DraftOverDeliveryApprovals_Drafts_CompanyId_DraftId",
                        columns: x => new { x.CompanyId, x.DraftId },
                        principalSchema: "sales",
                        principalTable: "Drafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftOverDeliveryApprovals_CompanyId_DraftId_LineNo",
                schema: "sales",
                table: "DraftOverDeliveryApprovals",
                columns: new[] { "CompanyId", "DraftId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "UX_DraftOverDeliveryApprovals_DraftVersion",
                schema: "sales",
                table: "DraftOverDeliveryApprovals",
                columns: new[] { "CompanyId", "DraftId", "DraftVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SAT-D30 / SAT-D213 forward fix: a recorded over-delivery approval is approval history (who approved which shipped
            // total) that the realization axis reads; dropping the table would silently turn approved lines back into "onay
            // bekliyor" and lose the audit trail. Refuse (51213, used by no other migration) while any approval exists; an empty
            // table rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[DraftOverDeliveryApprovals])
                BEGIN
                    THROW 51213, 'Over-delivery approvals exist; AddDraftOverDeliveryApprovals is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "DraftOverDeliveryApprovals",
                schema: "sales");
        }
    }
}
