using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// D2 (Sinan #66 5749046401, verbatim: "Satış Destek kendi henüz onaylanmamış sipariş taslağını geri çekebilir/iptal
/// edebilir; bunun ayrı durum/komut izi korunur, silme olmaz."): the approval axis gains its fifth state, Withdrawn (5).
/// No column is added — the withdrawer and the time live in the decision columns and the STATE says it was a withdrawal —
/// so the only schema change is the CHECK constraint's fifth arm: a withdrawn row carries the decision, a submission
/// that is either whole or absent (withdrawn from Preparing), and an optional reason. Nothing is deleted, ever, by this
/// path: the row stays, the lines stay, the archived revisions stay.
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260921090000_AddDraftWithdrawal")]
public sealed class AddDraftWithdrawal : Migration
{
    private const string FourArms =
        "([ApprovalState] = 1 AND [SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
            + "OR ([ApprovalState] = 2 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
            + "OR ([ApprovalState] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL) "
            + "OR ([ApprovalState] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND ([RejectionReason] IS NULL OR LEN([RejectionReason]) > 0))";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Drafts_Approval",
            schema: "sales",
            table: "Drafts");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Drafts_Approval",
            schema: "sales",
            table: "Drafts",
            sql: SalesDbContext.ApprovalConstraint);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // D2's trail is a record ("silme olmaz"): a rollback that would make a withdrawn draft's row violate the four-arm
        // constraint is refused with this module's own error (51050, used by no other module) while any withdrawn draft
        // exists; a database with none rolls back freely. Forward fix, the AddDraftApproval (51045) / AddDraftRevisions
        // (51049) pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[Drafts] WHERE [ApprovalState] = 5)
                THROW 51050, 'Sales withdrawn drafts exist; AddDraftWithdrawal is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_Drafts_Approval",
            schema: "sales",
            table: "Drafts");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Drafts_Approval",
            schema: "sales",
            table: "Drafts",
            sql: FourArms);
    }
}
