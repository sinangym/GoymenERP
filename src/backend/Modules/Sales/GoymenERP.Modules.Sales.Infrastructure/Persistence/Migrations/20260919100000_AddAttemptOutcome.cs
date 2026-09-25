using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// Sales API slice, ADR-011 §7: an attempt is either a stored draft or a recorded terminal refusal
/// (the Product owner's stale-version 409), so that the same key always answers the same result.
/// <c>DraftId</c> becomes nullable, <c>Outcome</c> (1 draft / 2 refusal) and <c>RefusalJson</c> are
/// added, the draft index is filtered to stored drafts. Every attempt row written before this
/// migration holds a stored draft (a refusal could not be recorded yet), so <c>Outcome</c> is added
/// with the transient default 1 for those rows and the default is dropped in the same migration:
/// the model has no default (the store always writes the outcome), and a row without an outcome
/// fails visibly. No released environment holds a Sales row (the host wires the module with this
/// slice); the backfill is there so that a stack migrated at #315 does not fail on a non-empty
/// table.
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260919100000_AddAttemptOutcome")]
public sealed class AddAttemptOutcome : Migration
{
    private const string NotEmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_DraftAttempts_Drafts_CompanyId_DraftId", schema: "sales", table: "DraftAttempts");
        migrationBuilder.DropIndex(name: "UX_DraftAttempts_DraftId", schema: "sales", table: "DraftAttempts");
        migrationBuilder.DropCheckConstraint(name: "CK_DraftAttempts_Identity", schema: "sales", table: "DraftAttempts");

        migrationBuilder.AlterColumn<Guid>(
            name: "DraftId",
            schema: "sales",
            table: "DraftAttempts",
            type: "uniqueidentifier",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier");

        // Backfill: every pre-§7 attempt is a stored draft (1); then the default goes, the model has none.
        migrationBuilder.AddColumn<int>(
            name: "Outcome",
            schema: "sales",
            table: "DraftAttempts",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AlterColumn<int>(
            name: "Outcome",
            schema: "sales",
            table: "DraftAttempts",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldDefaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "RefusalJson",
            schema: "sales",
            table: "DraftAttempts",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftAttempts_Identity",
            schema: "sales",
            table: "DraftAttempts",
            sql: "[CompanyId] <> " + NotEmptyGuid + " AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftAttempts_Outcome",
            schema: "sales",
            table: "DraftAttempts",
            sql: "([Outcome] = 1 AND [DraftId] IS NOT NULL AND [DraftId] <> " + NotEmptyGuid + " AND [RefusalJson] IS NULL) "
                + "OR ([Outcome] = 2 AND [DraftId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");

        migrationBuilder.AddForeignKey(
            name: "FK_DraftAttempts_Drafts_CompanyId_DraftId",
            schema: "sales",
            table: "DraftAttempts",
            columns: new[] { "CompanyId", "DraftId" },
            principalSchema: "sales",
            principalTable: "Drafts",
            principalColumns: new[] { "CompanyId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateIndex(
            name: "UX_DraftAttempts_DraftId",
            schema: "sales",
            table: "DraftAttempts",
            columns: new[] { "CompanyId", "DraftId" },
            unique: true,
            filter: "[DraftId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "UX_DraftAttempts_DraftId", schema: "sales", table: "DraftAttempts");
        migrationBuilder.DropForeignKey(name: "FK_DraftAttempts_Drafts_CompanyId_DraftId", schema: "sales", table: "DraftAttempts");
        migrationBuilder.DropCheckConstraint(name: "CK_DraftAttempts_Outcome", schema: "sales", table: "DraftAttempts");
        migrationBuilder.DropCheckConstraint(name: "CK_DraftAttempts_Identity", schema: "sales", table: "DraftAttempts");
        migrationBuilder.DropColumn(name: "RefusalJson", schema: "sales", table: "DraftAttempts");
        migrationBuilder.DropColumn(name: "Outcome", schema: "sales", table: "DraftAttempts");

        // A recorded refusal has no draft: with such a row stored this fails visibly.
        migrationBuilder.AlterColumn<Guid>(
            name: "DraftId",
            schema: "sales",
            table: "DraftAttempts",
            type: "uniqueidentifier",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_DraftAttempts_Identity",
            schema: "sales",
            table: "DraftAttempts",
            sql: "[CompanyId] <> " + NotEmptyGuid + " AND LEN([CommandName]) > 0 "
                + "AND [DraftId] <> " + NotEmptyGuid + " AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");

        migrationBuilder.AddForeignKey(
            name: "FK_DraftAttempts_Drafts_CompanyId_DraftId",
            schema: "sales",
            table: "DraftAttempts",
            columns: new[] { "CompanyId", "DraftId" },
            principalSchema: "sales",
            principalTable: "Drafts",
            principalColumns: new[] { "CompanyId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateIndex(
            name: "UX_DraftAttempts_DraftId",
            schema: "sales",
            table: "DraftAttempts",
            columns: new[] { "CompanyId", "DraftId" },
            unique: true);
    }
}
