using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A1-1 (plan API-01, TEK-09; ALS-D2, No.161 §1): the purchase request's Approved (3) and Rejected (4) states with
    /// their decision facts — the submitter (the second maker of hazırlayan ≠ onaylayan), the approval and the rejection
    /// with its reason — and two checks: the facts match the status, and no decision comes from a maker.
    /// <para>
    /// Up records the submitter of every already submitted request from its own recorded submit attempt
    /// (<c>purchasing.PurchaseAttempts</c>, the stored outcome of <c>purchasing.purchase-request.submit.v1</c>: a fact the
    /// module already holds, nothing guessed). A submitted request without such an attempt keeps no submitter, and the
    /// domain refuses every decision on it (fail-closed, <c>purchasing_request_maker_unknown</c>).
    /// </para>
    /// <para>
    /// Down refuses (<c>THROW 51164</c>, used by no other migration) while any request is Approved or Rejected: those
    /// states and their facts cannot be represented before this migration (forward fix). Otherwise it drops the new
    /// columns and checks; a recorded submitter is dropped with its column (the attempt still holds it).
    /// </para>
    /// </summary>
    public partial class AddPurchaseRequestDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequests_Status",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                schema: "purchasing",
                table: "PurchaseRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                schema: "purchasing",
                table: "PurchaseRequests",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAtUtc",
                schema: "purchasing",
                table: "PurchaseRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedBy",
                schema: "purchasing",
                table: "PurchaseRequests",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "purchasing",
                table: "PurchaseRequests",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmittedBy",
                schema: "purchasing",
                table: "PurchaseRequests",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE r
                SET r.[SubmittedBy] = a.[ActorId]
                FROM [purchasing].[PurchaseRequests] AS r
                INNER JOIN [purchasing].[PurchaseAttempts] AS a
                    ON a.[CompanyId] = r.[CompanyId] AND a.[RequestId] = r.[Id]
                WHERE r.[Status] = 2 AND r.[SubmittedBy] IS NULL
                    AND a.[CommandName] = N'purchasing.purchase-request.submit.v1' AND a.[Outcome] = 1;
                """);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseRequests] WHERE [Status] IN (3, 4))
                BEGIN
                    THROW 51164, 'Approved or rejected purchase requests exist; AddPurchaseRequestDecisions is not rolled back (forward fix).', 1;
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

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                schema: "purchasing",
                table: "PurchaseRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequests_Status",
                schema: "purchasing",
                table: "PurchaseRequests",
                sql: "[Status] IN (1, 2)");
        }
    }
}
