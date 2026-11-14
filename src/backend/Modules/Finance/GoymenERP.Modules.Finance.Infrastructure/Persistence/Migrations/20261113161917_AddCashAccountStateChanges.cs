using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashAccountStateChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CashAccounts_LiveName",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashAccounts_Decision",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChangeDecidedAtUtc",
                schema: "finance",
                table: "CashAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeDecidedBy",
                schema: "finance",
                table: "CashAccounts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<int>(
                name: "ChangeKind",
                schema: "finance",
                table: "CashAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChangeProposedAtUtc",
                schema: "finance",
                table: "CashAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeProposedBy",
                schema: "finance",
                table: "CashAccounts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "ChangeRejectionReason",
                schema: "finance",
                table: "CashAccounts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChangeStatus",
                schema: "finance",
                table: "CashAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_CashAccounts_LiveName",
                schema: "finance",
                table: "CashAccounts",
                columns: new[] { "CompanyId", "DisplayName" },
                unique: true,
                filter: "[Status] IN (1, 2, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashAccounts_Decision",
                schema: "finance",
                table: "CashAccounts",
                sql: "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] IN (2, 4) AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashAccounts_StateChange",
                schema: "finance",
                table: "CashAccounts",
                sql: "([ChangeKind] IS NULL AND [ChangeStatus] IS NULL AND [ChangeProposedBy] IS NULL AND [ChangeProposedAtUtc] IS NULL AND [ChangeDecidedBy] IS NULL AND [ChangeDecidedAtUtc] IS NULL AND [ChangeRejectionReason] IS NULL AND [Status] IN (1, 2, 3)) OR ([ChangeKind] IS NOT NULL AND [ChangeStatus] IS NOT NULL AND [ChangeKind] IN (1, 2) AND [ChangeProposedBy] IS NOT NULL AND LEN([ChangeProposedBy]) > 0 AND [ChangeProposedAtUtc] IS NOT NULL AND (([ChangeStatus] = 1 AND [ChangeDecidedBy] IS NULL AND [ChangeDecidedAtUtc] IS NULL AND [ChangeRejectionReason] IS NULL AND (([ChangeKind] = 1 AND [Status] = 2) OR ([ChangeKind] = 2 AND [Status] = 4))) OR ([ChangeStatus] = 2 AND [ChangeDecidedBy] IS NOT NULL AND [ChangeDecidedAtUtc] IS NOT NULL AND LEN([ChangeDecidedBy]) > 0 AND [ChangeDecidedBy] COLLATE Latin1_General_100_BIN2 <> [ChangeProposedBy] COLLATE Latin1_General_100_BIN2 AND [ChangeRejectionReason] IS NULL AND (([ChangeKind] = 1 AND [Status] = 4) OR ([ChangeKind] = 2 AND [Status] = 2))) OR ([ChangeStatus] = 3 AND [ChangeDecidedBy] IS NOT NULL AND [ChangeDecidedAtUtc] IS NOT NULL AND LEN([ChangeDecidedBy]) > 0 AND [ChangeDecidedBy] COLLATE Latin1_General_100_BIN2 <> [ChangeProposedBy] COLLATE Latin1_General_100_BIN2 AND [ChangeRejectionReason] IS NOT NULL AND LEN([ChangeRejectionReason]) > 0 AND (([ChangeKind] = 1 AND [Status] = 2) OR ([ChangeKind] = 2 AND [Status] = 4)))))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Finance's module refusal (51051), forward fix: a passive account cannot go back under the old checks,
            // and dropping the change columns would lose who proposed and who decided a passivation or reactivation.
            // A store whose accounts never left their opening decision rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[CashAccounts] WHERE [Status] = 4 OR [ChangeKind] IS NOT NULL)
                BEGIN
                    THROW 51051, 'Finance cash account passivations or reactivations exist; AddCashAccountStateChanges is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "UX_CashAccounts_LiveName",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashAccounts_Decision",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashAccounts_StateChange",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropColumn(
                name: "ChangeDecidedAtUtc",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropColumn(
                name: "ChangeDecidedBy",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropColumn(
                name: "ChangeKind",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropColumn(
                name: "ChangeProposedAtUtc",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropColumn(
                name: "ChangeProposedBy",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropColumn(
                name: "ChangeRejectionReason",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.DropColumn(
                name: "ChangeStatus",
                schema: "finance",
                table: "CashAccounts");

            migrationBuilder.CreateIndex(
                name: "UX_CashAccounts_LiveName",
                schema: "finance",
                table: "CashAccounts",
                columns: new[] { "CompanyId", "DisplayName" },
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashAccounts_Decision",
                schema: "finance",
                table: "CashAccounts",
                sql: "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0)");
        }
    }
}
