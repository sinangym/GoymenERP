using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// SINAN_KARARLARI_2026-10-21 No 30 ("Zorla kapatma … gerekçe zorunlu, audit … kalan 'iptal edilen kalan' olarak kaydedilir;
/// stok/para olayı yok") as narrowed by SAT-D12: an approved order's force-closure. The header's lifecycle <c>State</c> may now
/// be Closed (3), and only beside an approved order (CK_Drafts_State); ONE new append-only table records who closed, when, why,
/// the revision closed, the version produced, the impact preview the user confirmed and every line's closed remainder. No
/// existing column changes and no row is rewritten: the approval facts, lines (their quantities included), revisions and
/// schedules stay. The write guard admits no column of the new table.
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20261025120000_AddDraftForceClosures")]
public sealed class AddDraftForceClosures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts");

        migrationBuilder.CreateTable(
            name: "DraftForceClosures",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                ClosedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                ImpactToken = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ImpactJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                LinesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftForceClosures", x => new { x.CompanyId, x.DraftId });
                table.CheckConstraint("CK_DraftForceClosures_Identity", SalesDbContext.ForceClosureIdentityConstraint);
                table.CheckConstraint("CK_DraftForceClosures_Impact", "ISJSON([ImpactJson]) = 1");
                table.CheckConstraint("CK_DraftForceClosures_Lines", "ISJSON([LinesJson]) = 1");
                table.ForeignKey(
                    name: "FK_DraftForceClosures_Drafts_CompanyId_DraftId",
                    columns: x => new { x.CompanyId, x.DraftId },
                    principalSchema: "sales",
                    principalTable: "Drafts",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts",
            sql: SalesDbContext.StateConstraint);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No 30: a force-closure is a record (audit, "iptal edilen kalan"): dropping the table would lose who closed, why and the
        // closed remainders, and a closed header would violate the previous constraint. While any closure exists the rollback is
        // refused with this module's own error (51061, used by no other migration on main); an empty table rolls back freely.
        // The THROW precedes every destructive operation. Forward fix, the AddDraftCancellations (51055) pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[DraftForceClosures]) OR EXISTS (SELECT 1 FROM [sales].[Drafts] WHERE [State] = 3)
                THROW 51061, 'Sales order force-closures exist; AddDraftForceClosures is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropTable(
            name: "DraftForceClosures",
            schema: "sales");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts",
            sql: SalesDbContext.StateConstraintBeforeForceClose);
    }
}
