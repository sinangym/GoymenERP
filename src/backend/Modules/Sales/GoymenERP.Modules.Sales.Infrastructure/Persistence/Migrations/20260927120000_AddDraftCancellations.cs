using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// DEC-SAL-005 (KILITLI: "Sipariş iptalinde … etkisi gösterilir"; "Etki önizlemesi olmadan iptal yok") and AT-007 ("İptal ve
/// ters işlem geçmişi silmez"): an approved order's cancellation. The header's lifecycle <c>State</c> may now be Cancelled (2),
/// and only beside an approved order (CK_Drafts_State); ONE new append-only table records who cancelled, when, why, the
/// revision cancelled, the version produced and the impact preview the user confirmed. No existing column changes and no row
/// is rewritten: the approval facts, lines, revisions and schedules stay. The write guard admits no column of the new table.
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260927120000_AddDraftCancellations")]
public sealed class AddDraftCancellations : Migration
{
    private const string PreviousStateConstraint = "[Kind] = 1 AND [State] = 1 AND [Revision] >= 0";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts");

        migrationBuilder.CreateTable(
            name: "DraftCancellations",
            schema: "sales",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                CancelledBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                ImpactToken = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ImpactJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DraftCancellations", x => new { x.CompanyId, x.DraftId });
                table.CheckConstraint("CK_DraftCancellations_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND [DraftVersion] >= 5 AND LEN([CancelledBy]) > 0 AND LEN([Reason]) > 0 AND LEN([ImpactToken]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_DraftCancellations_Impact", "ISJSON([ImpactJson]) = 1");
                table.ForeignKey(
                    name: "FK_DraftCancellations_Drafts_CompanyId_DraftId",
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
        // AT-007: a cancellation is a record ("geçmişi silmez"): dropping the table would lose who cancelled and why, and a
        // cancelled header would violate the previous constraint. While any cancellation exists the rollback is refused with
        // this module's own error (51055, used by no other module on main); an empty database rolls back freely. The THROW
        // precedes every destructive operation. Forward fix, the AddDraftWithdrawal (51050) / AddDeliverySchedules (51054)
        // pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[DraftCancellations]) OR EXISTS (SELECT 1 FROM [sales].[Drafts] WHERE [State] <> 1)
                THROW 51055, 'Sales order cancellations exist; AddDraftCancellations is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropTable(
            name: "DraftCancellations",
            schema: "sales");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Drafts_State",
            schema: "sales",
            table: "Drafts",
            sql: PreviousStateConstraint);
    }
}
