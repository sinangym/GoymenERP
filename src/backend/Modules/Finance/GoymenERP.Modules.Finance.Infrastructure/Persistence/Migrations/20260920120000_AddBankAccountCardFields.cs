using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>
/// K2 (Sinan #66 <c>5748258299</c>): the bank account card gains bank name, account type, branch name
/// and code, IBAN and account number.
///
/// <para>
/// EVERY COLUMN IS NULLABLE, INCLUDING THE TWO K2 CALLS MANDATORY, and that is not a weakening. Rows
/// written before K2 have no value for them. A <c>NOT NULL</c> column would need a backfill, and any
/// value chosen for those rows — a placeholder, a blank, a guess — would be this migration answering
/// the open question <c>#66 5748390243</c> (what becomes of already-approved cards). It answers
/// nothing: no backfill, no default, no reinterpretation. The mandatory pair is enforced where a card
/// is WRITTEN, so a card created from now on cannot omit it, while the existing rows keep their NULLs
/// until a human decides what they mean.
/// </para>
///
/// <para>
/// NOTHING ELSE CHANGES. No index, no constraint, no status, and no money: this table holds the
/// company's own treasury cards and moves nothing. In particular <c>IsUsableForReceipt</c> still
/// derives from approval alone — K2's "a money-capable account must carry an IBAN" half is
/// deliberately NOT here, because it and the declaration it rests on are one change.
/// </para>
///
/// <para>
/// <see cref="Down"/> drops the six columns, which is the ordinary reversal of adding them: no money
/// fact and no command record is touched, and no row is deleted. What it does discard is card data
/// entered after this migration — so a rollback is a decision to lose bank names and IBANs typed in
/// between, and whoever takes it should re-enter them. It is written plainly rather than guarded,
/// because guarding it would itself be a retention rule, and retention is not this slice's to set.
/// </para>
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260920120000_AddBankAccountCardFields")]
public sealed class AddBankAccountCardFields : Migration
{
    private const string Schema = "finance";
    private const string Table = "BankAccounts";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BankName", schema: Schema, table: Table, type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "AccountType", schema: Schema, table: Table, type: "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "BranchName", schema: Schema, table: Table, type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "BranchCode", schema: Schema, table: Table, type: "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "Iban", schema: Schema, table: Table, type: "nvarchar(34)", maxLength: 34, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "AccountNumber", schema: Schema, table: Table, type: "nvarchar(64)", maxLength: 64, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AccountNumber", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "Iban", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "BranchCode", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "BranchName", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "AccountType", schema: Schema, table: Table);
        migrationBuilder.DropColumn(name: "BankName", schema: Schema, table: Table);
    }
}
