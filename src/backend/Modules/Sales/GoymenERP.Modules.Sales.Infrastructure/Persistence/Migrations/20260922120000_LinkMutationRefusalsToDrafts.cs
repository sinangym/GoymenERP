using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// D2 terminal mutation refusals retain the draft identity they acted on. Create-time refusals still
/// precede a draft and therefore keep a null <c>DraftId</c>. The existing composite foreign key keeps
/// every non-null identity company-scoped.
/// <para>
/// <c>Down</c> refuses once a linked mutation refusal exists because restoring the former constraint
/// would require deleting its durable identity; that environment needs a forward fix instead.
/// </para>
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260922120000_LinkMutationRefusalsToDrafts")]
public sealed class LinkMutationRefusalsToDrafts : Migration
{
    private const string LinkedOutcomeConstraint =
        "([Outcome] = 1 AND [DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) "
        + "OR ([Outcome] = 2 AND ([DraftId] IS NULL OR [DraftId] <> '00000000-0000-0000-0000-000000000000') "
        + "AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)";

    private const string PreviousOutcomeConstraint =
        "([Outcome] = 1 AND [DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) "
        + "OR ([Outcome] = 2 AND [DraftId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_DraftAttempts_Outcome",
            schema: "sales",
            table: "DraftAttempts");

        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftAttempts_Outcome",
            schema: "sales",
            table: "DraftAttempts",
            sql: LinkedOutcomeConstraint);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1
                FROM [sales].[DraftAttempts]
                WHERE [Outcome] = 2 AND [DraftId] IS NOT NULL)
                THROW 51046, 'Linked Sales mutation refusals require a forward fix.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_DraftAttempts_Outcome",
            schema: "sales",
            table: "DraftAttempts");

        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftAttempts_Outcome",
            schema: "sales",
            table: "DraftAttempts",
            sql: PreviousOutcomeConstraint);
    }
}
