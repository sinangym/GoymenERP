using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A1-1b (plan API-01b, TEK-09; ALS-D46, ALS-D47): the purchase request's Withdrawn (5) state — the preparer's or the
    /// submitter's withdrawal of a still Submitted request, a separate outcome from Rejected — with its facts (WithdrawnBy,
    /// WithdrawnAtUtc, WithdrawalReason) and a check that a Withdrawn request has exactly them, from exactly its preparer
    /// (CreatedBy) or recorded submitter, and any other status none of them. The decision check gains the Withdrawn branch
    /// (no approval or rejection; the submitter only if recorded, since the preparer may withdraw a request submitted
    /// before A1-1).
    /// <para>
    /// #900 review P3: the maker ≠ checker check is re-created with a binary comparison (<c>Latin1_General_100_BIN2</c>),
    /// the domain's ordinal rule; under the database's case-insensitive default a decider differing from a maker only by
    /// case passed the domain and failed the check. No stored row is changed (every existing row already satisfied the
    /// stricter case-insensitive inequality, which implies the binary one).
    /// </para>
    /// <para>
    /// Down refuses (<c>THROW 51165</c>, used by no other migration) while any request is Withdrawn: that state and its facts
    /// cannot be represented before this migration (forward fix). It also refuses (<c>THROW 51166</c>, used by no other
    /// migration) while an approver or rejecter equals a maker under the database's default (case-insensitive) collation —
    /// rows the binary check admits but the restored A1-1 check would reject with a raw 547. Otherwise it drops the new check
    /// and columns and restores the A1-1 checks exactly as <c>AddPurchaseRequestDecisions</c> created them.
    /// </para>
    /// </summary>
    public partial class AddPurchaseRequestWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequests_Decision",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequests_MakerChecker",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequests_Status",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.AddColumn<string>(
                name: "WithdrawalReason",
                schema: "purchasing",
                table: "PurchaseRequests",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WithdrawnAtUtc",
                schema: "purchasing",
                table: "PurchaseRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawnBy",
                schema: "purchasing",
                table: "PurchaseRequests",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequests_Decision",
                schema: "purchasing",
                table: "PurchaseRequests",
                sql: "([Status] = 1 AND [SubmittedBy] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 2 AND ([SubmittedBy] IS NULL OR LEN([SubmittedBy]) > 0) AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [RejectedBy] IS NOT NULL AND LEN([RejectedBy]) > 0 AND [RejectedAtUtc] IS NOT NULL AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND (([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL))) OR ([Status] = 5 AND ([SubmittedBy] IS NULL OR LEN([SubmittedBy]) > 0) AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequests_MakerChecker",
                schema: "purchasing",
                table: "PurchaseRequests",
                sql: "([ApprovedBy] IS NULL OR ([ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2 AND [SubmittedBy] IS NOT NULL AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [SubmittedBy] COLLATE Latin1_General_100_BIN2)) AND ([RejectedBy] IS NULL OR ([RejectedBy] COLLATE Latin1_General_100_BIN2 <> [CreatedBy] COLLATE Latin1_General_100_BIN2 AND [SubmittedBy] IS NOT NULL AND [RejectedBy] COLLATE Latin1_General_100_BIN2 <> [SubmittedBy] COLLATE Latin1_General_100_BIN2))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequests_Status",
                schema: "purchasing",
                table: "PurchaseRequests",
                sql: "[Status] IN (1, 2, 3, 4, 5)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequests_Withdrawal",
                schema: "purchasing",
                table: "PurchaseRequests",
                sql: "([Status] = 5 AND [WithdrawnBy] IS NOT NULL AND LEN([WithdrawnBy]) > 0 AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawalReason] IS NOT NULL AND LEN([WithdrawalReason]) > 0 AND ([WithdrawnBy] COLLATE Latin1_General_100_BIN2 = [CreatedBy] COLLATE Latin1_General_100_BIN2 OR ([SubmittedBy] IS NOT NULL AND [WithdrawnBy] COLLATE Latin1_General_100_BIN2 = [SubmittedBy] COLLATE Latin1_General_100_BIN2))) OR ([Status] <> 5 AND [WithdrawnBy] IS NULL AND [WithdrawnAtUtc] IS NULL AND [WithdrawalReason] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseRequests] WHERE [Status] = 5)
                BEGIN
                    THROW 51165, 'Withdrawn purchase requests exist; AddPurchaseRequestWithdrawal is not rolled back (forward fix).', 1;
                END;
                """);

            // #907 review P3: the restored A1-1 maker-checker check compares under the column's default collation; refuse
            // clearly, before any drop, when a stored decision would violate it (a decider equal to a maker but for case).
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseRequests]
                    WHERE ([ApprovedBy] IS NOT NULL AND ([ApprovedBy] = [CreatedBy] OR [ApprovedBy] = [SubmittedBy]))
                       OR ([RejectedBy] IS NOT NULL AND ([RejectedBy] = [CreatedBy] OR [RejectedBy] = [SubmittedBy])))
                BEGIN
                    THROW 51166, 'A purchase request decision differs from its maker only by case; AddPurchaseRequestWithdrawal is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequests_Decision",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequests_MakerChecker",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequests_Status",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequests_Withdrawal",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "WithdrawalReason",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "WithdrawnAtUtc",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "WithdrawnBy",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequests_Decision",
                schema: "purchasing",
                table: "PurchaseRequests",
                sql: "([Status] = 1 AND [SubmittedBy] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 2 AND ([SubmittedBy] IS NULL OR LEN([SubmittedBy]) > 0) AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [RejectedBy] IS NOT NULL AND LEN([RejectedBy]) > 0 AND [RejectedAtUtc] IS NOT NULL AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND (([ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([ApprovedBy] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedAtUtc] IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequests_MakerChecker",
                schema: "purchasing",
                table: "PurchaseRequests",
                sql: "([ApprovedBy] IS NULL OR ([ApprovedBy] <> [CreatedBy] AND [SubmittedBy] IS NOT NULL AND [ApprovedBy] <> [SubmittedBy])) AND ([RejectedBy] IS NULL OR ([RejectedBy] <> [CreatedBy] AND [SubmittedBy] IS NOT NULL AND [RejectedBy] <> [SubmittedBy]))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequests_Status",
                schema: "purchasing",
                table: "PurchaseRequests",
                sql: "[Status] IN (1, 2, 3, 4)");
        }
    }
}
