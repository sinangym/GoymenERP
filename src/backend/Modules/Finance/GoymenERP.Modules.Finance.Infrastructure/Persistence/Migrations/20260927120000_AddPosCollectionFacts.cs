using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>
/// DEC-FIN-014 (customer POS collection): a receipt source and its one money event gain the POS facts — the
/// commission, the bank net and who bears the commission — beside the GROSS amount they already hold. Every stored row
/// is a bank transfer, so the new columns are added NULL; <c>CK_ReceiptDrafts_Pos</c> and <c>CK_MoneyEvents_Pos</c>
/// then hold "no POS fact, or all three with a known bearer". No existing value is rewritten.
/// <see cref="Down"/> refuses with Finance's module refusal (<c>THROW 51051</c>) while any POS fact is stored: a
/// rollback would lose the commission and net of a real collection. With none it drops exactly what Up added.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260927120000_AddPosCollectionFacts")]
public sealed class AddPosCollectionFacts : Migration
{
    // Written out, not read from the model: this migration's text never changes with the model.
    private const string PosCheck =
        "([PosCommissionAmountText] IS NULL AND [PosBankNetAmountText] IS NULL AND [PosCommissionBearer] IS NULL) OR "
        + "([PosCommissionAmountText] IS NOT NULL AND LEN([PosCommissionAmountText]) > 0 "
        + "AND [PosBankNetAmountText] IS NOT NULL AND LEN([PosBankNetAmountText]) > 0 "
        + "AND [PosCommissionBearer] IN (1, 2))";

    private static readonly string[] Tables = ["ReceiptDrafts", "MoneyEvents"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.AddColumn<string>(
                name: "PosCommissionAmountText",
                schema: "finance",
                table: table,
                type: "varchar(max)",
                unicode: false,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PosBankNetAmountText",
                schema: "finance",
                table: table,
                type: "varchar(max)",
                unicode: false,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PosCommissionBearer",
                schema: "finance",
                table: table,
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: $"CK_{table}_Pos",
                schema: "finance",
                table: table,
                sql: PosCheck);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Finance's module refusal (51051): a rollback never drops a POS commission or bank net that was stored.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [finance].[ReceiptDrafts] WHERE [PosCommissionBearer] IS NOT NULL)
               OR EXISTS (SELECT 1 FROM [finance].[MoneyEvents] WHERE [PosCommissionBearer] IS NOT NULL)
                THROW 51051, 'Finance POS collection facts exist; AddPosCollectionFacts is not rolled back (forward fix).', 1;
            """);

        foreach (var table in Tables)
        {
            migrationBuilder.DropCheckConstraint(name: $"CK_{table}_Pos", schema: "finance", table: table);
            migrationBuilder.DropColumn(name: "PosCommissionBearer", schema: "finance", table: table);
            migrationBuilder.DropColumn(name: "PosBankNetAmountText", schema: "finance", table: table);
            migrationBuilder.DropColumn(name: "PosCommissionAmountText", schema: "finance", table: table);
        }
    }
}
