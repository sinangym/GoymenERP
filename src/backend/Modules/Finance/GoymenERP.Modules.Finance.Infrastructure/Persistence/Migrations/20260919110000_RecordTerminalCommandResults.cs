using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;

/// <summary>
/// FIN-UI-1, ADR-011 §7: an authorized terminal refusal is the key's result for good, so it is kept
/// in the command record. A refusal whose target does not exist (a draft or bank account id that is
/// not the company's) has nothing to point at: <c>CK_Commands_Target</c> now also admits a record
/// with no target, and only when its stored result is a refusal (<c>ValidationFailed</c>), never a
/// success. Nothing else changes. <see cref="Down"/> restores the stricter rule; with a target-less
/// row stored it fails visibly, and no row is deleted.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260919110000_RecordTerminalCommandResults")]
public sealed class RecordTerminalCommandResults : Migration
{
    private const string ExactlyOneTarget =
        "([DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] IS NULL) OR "
        + "([BankAccountId] IS NOT NULL AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] IS NULL)";

    // Written out, not read from the model: this migration's text never changes with the model.
    private const string OneTargetOrATargetlessRefusal = ExactlyOneTarget + " OR "
        + "([DraftId] IS NULL AND [BankAccountId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Target",
            schema: "finance",
            table: "Commands",
            sql: OneTargetOrATargetlessRefusal);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_Commands_Target", schema: "finance", table: "Commands");
        migrationBuilder.AddCheckConstraint(
            name: "CK_Commands_Target",
            schema: "finance",
            table: "Commands",
            sql: ExactlyOneTarget);
    }
}
