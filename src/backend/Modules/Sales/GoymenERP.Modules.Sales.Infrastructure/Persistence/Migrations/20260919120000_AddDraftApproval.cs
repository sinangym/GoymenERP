using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;

/// <summary>
/// SALES-A1: the internal approval axis of a draft (DEC-SAL-001; DURUM_MAKINELERI "İç onay").
/// <c>ApprovalState</c> (1 Preparing, 2 PendingApproval, 3 Approved, 4 Rejected) and the approval
/// facts (who submitted / decided, when, the rejection reason) join the header; the check constraint
/// binds the facts to the state exactly. Every draft stored before this migration was never submitted
/// (no command existed), so <c>ApprovalState</c> is added with the transient default 1 for those rows
/// and the default is dropped in the same migration — the model has none (the store always writes
/// the state), the Outcome pattern of <c>AddAttemptOutcome</c>. No released environment holds a Sales
/// row; the backfill is for a stack migrated earlier. A filtered list index serves the state filter.
/// <para>
/// The state commands record their own attempts against the draft they act on, so the "one attempt
/// per stored draft" uniqueness (<c>UX_DraftAttempts_DraftId</c>) is narrowed to the create command:
/// a draft is still born from exactly one create attempt; submit / approve / reject attempts share
/// its id, one per key.
/// </para>
/// <para>
/// <c>Down</c> refuses (THROW 51045) while any draft has left <c>Preparing</c>: dropping the columns
/// would silently forget who submitted, approved or rejected an order — a forward fix, never a
/// silent loss (the module rule of the other owners' Down guards).
/// </para>
/// </summary>
[DbContext(typeof(SalesDbContext))]
[Migration("20260919120000_AddDraftApproval")]
public sealed class AddDraftApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Backfill: every pre-A1 draft is Preparing (1); then the default goes, the model has none.
        migrationBuilder.AddColumn<int>(
            name: "ApprovalState",
            schema: "sales",
            table: "Drafts",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AlterColumn<int>(
            name: "ApprovalState",
            schema: "sales",
            table: "Drafts",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldDefaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "SubmittedBy",
            schema: "sales",
            table: "Drafts",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SubmittedAtUtc",
            schema: "sales",
            table: "Drafts",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DecidedBy",
            schema: "sales",
            table: "Drafts",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DecidedAtUtc",
            schema: "sales",
            table: "Drafts",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RejectionReason",
            schema: "sales",
            table: "Drafts",
            type: "nvarchar(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Drafts_Approval",
            schema: "sales",
            table: "Drafts",
            sql: SalesDbContext.ApprovalConstraint);

        migrationBuilder.CreateIndex(
            name: "IX_Drafts_CompanyId_ApprovalState_CreatedAtUtc_Id",
            schema: "sales",
            table: "Drafts",
            columns: new[] { "CompanyId", "ApprovalState", "CreatedAtUtc", "Id" });

        // One create attempt per stored draft; the state commands' attempts share the draft id.
        migrationBuilder.DropIndex(name: "UX_DraftAttempts_DraftId", schema: "sales", table: "DraftAttempts");
        migrationBuilder.CreateIndex(
            name: "UX_DraftAttempts_DraftId",
            schema: "sales",
            table: "DraftAttempts",
            columns: new[] { "CompanyId", "DraftId" },
            unique: true,
            filter: SalesDbContext.CreateAttemptFilter);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Refuses while any draft has left Preparing or any state command has recorded an attempt: the
        // pre-A1 index (one attempt per draft) could not hold those rows either.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [sales].[Drafts] WHERE [ApprovalState] <> 1)
                OR EXISTS (SELECT 1 FROM [sales].[DraftAttempts] WHERE [DraftId] IS NOT NULL AND [CommandName] <> N'sales.order-draft.create.v1')
                THROW 51045, 'Sales internal approval facts require a forward fix.', 1;
            """);

        migrationBuilder.DropIndex(name: "UX_DraftAttempts_DraftId", schema: "sales", table: "DraftAttempts");
        migrationBuilder.CreateIndex(
            name: "UX_DraftAttempts_DraftId",
            schema: "sales",
            table: "DraftAttempts",
            columns: new[] { "CompanyId", "DraftId" },
            unique: true,
            filter: "[DraftId] IS NOT NULL");

        migrationBuilder.DropIndex(
            name: "IX_Drafts_CompanyId_ApprovalState_CreatedAtUtc_Id",
            schema: "sales",
            table: "Drafts");

        migrationBuilder.DropCheckConstraint(name: "CK_Drafts_Approval", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "RejectionReason", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "DecidedAtUtc", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "DecidedBy", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "SubmittedAtUtc", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "SubmittedBy", schema: "sales", table: "Drafts");
        migrationBuilder.DropColumn(name: "ApprovalState", schema: "sales", table: "Drafts");
    }
}
