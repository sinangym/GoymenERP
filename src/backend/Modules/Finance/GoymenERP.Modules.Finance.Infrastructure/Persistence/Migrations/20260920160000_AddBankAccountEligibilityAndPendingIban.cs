using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>
/// B3 (Sinan #66 <c>5750700247</c>) and D4 (#66 <c>5749046401</c>), in ONE migration because both land
/// on the same table: the card gains its money-movement declaration, and an approved account gains a
/// place to hold a proposed IBAN while that change waits for its own Ö4 approval.
///
/// <para>
/// <b><c>MoneyMovementEligible</c> IS NULLABLE AND NOTHING IS BACKFILLED — that is the decision, not a
/// concession.</b> B3 says a legacy row whose declaration is NULL is not eligible for payment or
/// collection, and that "banka adı+tür+IBAN'ın dolu olması bu açık beyanın yerine geçmez". A
/// <c>NOT NULL</c> column would need a value for the existing rows, and every candidate value is this
/// migration answering a business question: <c>false</c> would silently reclassify cards a human never
/// declared, and <c>true</c> would derive eligibility from data, which is precisely the reading Sinan
/// rejected. So the column carries NULL forward and the product reads NULL as UNDECLARED — a state the
/// user is shown specifically ("para hareketine uygunluk beyanı tamamlanmamış"), not folded into a no.
/// No row is deleted, no identity changes, and no historical transaction is touched.
/// </para>
///
/// <para>
/// <b>The four pending-IBAN columns are a place to wait, not a second IBAN.</b> The account's own
/// <c>Iban</c> is untouched while a change is pending, which is how "eski onaylı değer korunur" and
/// "değişen IBAN onaylanmadan yeni para işleminde kullanılamaz" both hold with no new refusal point:
/// the money path reads the approved column, and the approved column has not moved. The check
/// constraint keeps the four all-null or all-set, so a half-written pending change — an IBAN with no
/// proposer for Ö4 to be different from — cannot exist in the table.
/// </para>
///
/// <para>
/// <b>What this migration deliberately does NOT do:</b> it adds no constraint tying the declaration to
/// the IBAN column. "A money-eligible card carries a valid IBAN" is enforced where a card is written,
/// not by the table, because the table also holds rows written before either rule existed and a check
/// constraint would reject them on the next update — which would be this migration deciding the fate of
/// legacy rows after all.
/// </para>
///
/// <para>
/// <see cref="Down"/> drops the check constraint and the five columns, the ordinary reversal of adding
/// them. It discards declarations and pending changes entered after this migration: a rollback loses
/// the statement that a card may move money, and an account whose IBAN change had not yet been approved
/// returns to its approved IBAN — which is the SAFE direction (no unapproved value can survive into the
/// money path), but it is still a loss, so whoever rolls back re-enters them. Written plainly rather
/// than guarded: guarding it would itself be a retention rule, and retention is not this slice's to set.
/// </para>
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260920160000_AddBankAccountEligibilityAndPendingIban")]
public sealed class AddBankAccountEligibilityAndPendingIban : Migration
{
    private const string Schema = "finance";
    private const string Table = "BankAccounts";
    private const string PendingIbanCheckName = "CK_BankAccounts_PendingIbanChange";
    private const string IdentityCollation = "Latin1_General_100_BIN2";

    /// <summary>
    /// A FROZEN COPY of <c>FinanceDbContext.PendingIbanCheck</c>, deliberately not a reference to it —
    /// the precedent of <c>CK_ReceiptDrafts_Stage</c> in <c>20260920090000_AddMoneyEvents</c>. A
    /// migration is a record of what was applied; if it read a live constant, editing that constant
    /// would retroactively change what an already-applied migration says it did. The two texts must
    /// match, and a drift between them shows up as a model difference rather than being hidden.
    /// </summary>
    private const string PendingIbanCheck =
        "([PendingIban] IS NULL AND [PendingIbanProposedBy] IS NULL AND [PendingIbanProposedAtUtc] IS NULL "
        + "AND [PendingIbanReason] IS NULL) OR "
        + "([PendingIban] IS NOT NULL AND [PendingIbanProposedBy] IS NOT NULL AND LEN([PendingIbanProposedBy]) > 0 "
        + "AND [PendingIbanProposedAtUtc] IS NOT NULL AND [PendingIbanReason] IS NOT NULL AND LEN([PendingIbanReason]) > 0)";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "MoneyMovementEligible", schema: Schema, table: Table, type: "bit", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "PendingIban", schema: Schema, table: Table, type: "nvarchar(34)", maxLength: 34, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "PendingIbanProposedBy", schema: Schema, table: Table, type: "nvarchar(128)", maxLength: 128,
            nullable: true, collation: IdentityCollation);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PendingIbanProposedAtUtc", schema: Schema, table: Table, type: "datetimeoffset", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "PendingIbanReason", schema: Schema, table: Table, type: "nvarchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: PendingIbanCheckName, schema: Schema, table: Table, sql: PendingIbanCheck);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: PendingIbanCheckName, schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "PendingIbanReason", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "PendingIbanProposedAtUtc", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "PendingIbanProposedBy", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "PendingIban", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "MoneyMovementEligible", schema: Schema, table: Table);
    }
}
