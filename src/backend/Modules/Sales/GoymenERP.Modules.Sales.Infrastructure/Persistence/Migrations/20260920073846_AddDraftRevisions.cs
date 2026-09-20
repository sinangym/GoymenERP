using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

[DbContext(typeof(SalesDbContext))]
[Migration("20260920073846_AddDraftRevisions")]
public sealed class AddDraftRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Provenance — Sinan, Issue #66 comment 5748258299 (K3), verbatim: "Onaylı fiyat/iskonto değişikliği yeni revizyon +
        // yeniden onaya girer, eski onaylı snapshot silinmez." So revisions EXIST from this decision on, and the header's
        // Revision is a counter (0 at creation, +1 per archived approved revision). The initial migration (SALES-P2, R3)
        // pinned "[Revision] = 0" into CK_Drafts_State as a statement about the world at that time — no revisions yet —
        // and that premise is superseded by the decision above; widening it to ">= 0" removes the superseded pin, it does
        // not choose a policy. Kind and State stay pinned exactly as they were (nothing supersedes them). The same shape
        // already exists in Logistics: CK_LoadingInstructionLines_Revision "[SourceOrderRevision] >= 0" (a revision
        // counter constrained to non-negative, not to a value). The real-SQL revision case is what found the pin.
        migrationBuilder.DropCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts",
            sql: "[Kind] = 1 AND [State] = 1 AND [Revision] >= 0");

        migrationBuilder.CreateTable(
            name: "DraftRevisions",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                SubmittedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                MoneyProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MoneyProfileVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                MoneyScale = table.Column<int>(type: "int", nullable: false),
                ArchivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ArchivedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftRevisions", x => new { x.CompanyId, x.DraftId, x.Revision });
                table.CheckConstraint("CK_DraftRevisions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND LEN([SubmittedBy]) > 0 AND LEN([ApprovedBy]) > 0 AND LEN([ArchivedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_DraftRevisions_Money", "[MoneyProfileId] <> '00000000-0000-0000-0000-000000000000' AND LEN([MoneyProfileVersion]) > 0 AND [MoneyScale] >= 0");
                table.ForeignKey(
                    name: "FK_DraftRevisions_Drafts_CompanyId_DraftId",
                    columns: x => new { x.CompanyId, x.DraftId },
                    principalSchema: "sales",
                    principalTable: "Drafts",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DraftRevisionLines",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                OrderUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                QuantityText = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                UnitPrice = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                PriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                DiscountPercent = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftRevisionLines", x => new { x.CompanyId, x.DraftId, x.Revision, x.LineNo });
                table.CheckConstraint("CK_DraftRevisionLines_Identity", "[LineNo] >= 1 AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND LEN([QuantityText]) > 0");
                table.CheckConstraint("CK_DraftRevisionLines_Price", "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL) OR ([UnitPrice] IS NOT NULL AND [PriceUnitId] IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_DraftRevisionLines_DraftRevisions_CompanyId_DraftId_Revision",
                    columns: x => new { x.CompanyId, x.DraftId, x.Revision },
                    principalSchema: "sales",
                    principalTable: "DraftRevisions",
                    principalColumns: new[] { "CompanyId", "DraftId", "Revision" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "UX_DraftRevisionLines_LineId",
            schema: "sales",
            table: "DraftRevisionLines",
            columns: new[] { "CompanyId", "DraftId", "Revision", "LineId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // K3 (Sinan 5748258299): "the previous approved snapshot is not deleted" — a rollback that would drop archived
        // approved revisions is refused with the module's own error (51049, used by no other module); the empty tables of a
        // database that never archived one roll back freely. Forward fix, the AddDraftApproval (51045) pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[DraftRevisions])
                THROW 51049, 'Sales archived approved revisions exist; AddDraftRevisions is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropTable(
            name: "DraftRevisionLines",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "DraftRevisions",
            schema: "sales");

        // The "= 0" pin is restored only past the guard above, and that ordering carries a DEPENDENCY on the service code
        // of this head: SalesStore.ReviseDraftCommercialAsync archives the approved revision and advances the header's
        // Revision in ONE SaveChanges, and no other path writes Revision (the context's write guard admits it only there).
        // Under that invariant a header with Revision > 0 exists only beside an archived row, so a database that passed
        // the 51049 guard has no such header and the restored "= 0" constraint holds. A Down runs against whatever data
        // exists: if a later head ever writes Revision without archiving, this restore is what fails (the ALTER refuses),
        // and that head owes its own guard here. No second guard is added on purpose — one refusal, one reason.
        migrationBuilder.DropCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts",
            sql: "[Kind] = 1 AND [State] = 1 AND [Revision] = 0");
    }
}
