using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessGrantRequestWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_DecisionState",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAtUtc",
                schema: "identity",
                table: "RoleAssignments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RevokedByUserId",
                schema: "identity",
                table: "RoleAssignments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionReasonCode",
                schema: "identity",
                table: "AccessGrantRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "identity",
                table: "AccessGrantRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResultingRoleAssignmentId",
                schema: "identity",
                table: "AccessGrantRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidFromUtc",
                schema: "identity",
                table: "AccessGrantRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidUntilUtc",
                schema: "identity",
                table: "AccessGrantRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_RevokedByUserId",
                schema: "identity",
                table: "RoleAssignments",
                column: "RevokedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RoleAssignments_Revocation",
                schema: "identity",
                table: "RoleAssignments",
                sql: "([RevokedByUserId] IS NULL AND [RevokedAtUtc] IS NULL) OR ([Status] = N'Revoked' AND [RevokedByUserId] IS NOT NULL AND [RevokedAtUtc] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AccessGrantRequests_CompanyId_ResultingRoleAssignmentId",
                schema: "identity",
                table: "AccessGrantRequests",
                columns: new[] { "CompanyId", "ResultingRoleAssignmentId" });

            migrationBuilder.CreateIndex(
                name: "UX_AccessGrantRequests_Pending",
                schema: "identity",
                table: "AccessGrantRequests",
                columns: new[] { "CompanyId", "TargetUserId", "RoleId" },
                unique: true,
                filter: "[Status] = N'Pending'");

            migrationBuilder.CreateIndex(
                name: "UX_AccessGrantRequests_ResultingRoleAssignment",
                schema: "identity",
                table: "AccessGrantRequests",
                column: "ResultingRoleAssignmentId",
                unique: true,
                filter: "[ResultingRoleAssignmentId] IS NOT NULL");

            // Requests approved before this migration could only have been approved by a second person
            // (the old CK_AccessGrantRequests_SecondApproval); record that mode before its check holds.
            migrationBuilder.Sql(
                """
                UPDATE [identity].[AccessGrantRequests]
                SET [ApprovalMode] = N'SecondApproval'
                WHERE [Status] = N'Approved' AND [ApprovalMode] IS NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "([Status] = N'Approved' AND [ApprovalMode] IN (N'SecondApproval', N'FounderDirect')) OR ([Status] <> N'Approved' AND [ApprovalMode] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_DecisionReason",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[DecisionReasonCode] IS NULL OR [Status] = N'Rejected'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_DecisionState",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "([Status] = N'Pending' AND [DecidedByUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR ([Status] IN (N'Approved', N'Rejected', N'Withdrawn') AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_ResultingAssignment",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[ResultingRoleAssignmentId] IS NULL OR [Status] = N'Approved'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[RequiresSecondApproval] = 0 OR [DecidedByUserId] IS NULL OR ([Status] = N'Withdrawn' AND [DecidedByUserId] = [RequestedByUserId]) OR ([Status] = N'Approved' AND [ApprovalMode] = N'FounderDirect' AND [DecidedByUserId] = [RequestedByUserId]) OR ([DecidedByUserId] <> [RequestedByUserId] AND [DecidedByUserId] <> [TargetUserId])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_Validity",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[ValidUntilUtc] IS NULL OR ([ValidFromUtc] IS NOT NULL AND [ValidUntilUtc] > [ValidFromUtc])");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessGrantRequests_RoleAssignments_CompanyId_ResultingRoleAssignmentId",
                schema: "identity",
                table: "AccessGrantRequests",
                columns: new[] { "CompanyId", "ResultingRoleAssignmentId" },
                principalSchema: "identity",
                principalTable: "RoleAssignments",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleAssignments_Users_RevokedByUserId",
                schema: "identity",
                table: "RoleAssignments",
                column: "RevokedByUserId",
                principalSchema: "identity",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: rolling back would drop who revoked an assignment and when, withdrawn and founder-direct
            // decisions, rejection reasons, request windows and reasons, and the request-to-assignment link.
            // ApprovalMode 'SecondApproval' alone is reconstructible (the Up backfill), so it does not refuse.
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM [identity].[RoleAssignments]
                    WHERE [RevokedByUserId] IS NOT NULL OR [RevokedAtUtc] IS NOT NULL)
                OR EXISTS (
                    SELECT 1 FROM [identity].[AccessGrantRequests]
                    WHERE [Status] = N'Withdrawn'
                        OR [ApprovalMode] = N'FounderDirect'
                        OR [DecisionReasonCode] IS NOT NULL
                        OR [Reason] IS NOT NULL
                        OR [ResultingRoleAssignmentId] IS NOT NULL
                        OR [ValidFromUtc] IS NOT NULL
                        OR [ValidUntilUtc] IS NOT NULL)
                BEGIN
                    THROW 51484, 'Access grant workflow data exists; AddAccessGrantRequestWorkflow is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_AccessGrantRequests_RoleAssignments_CompanyId_ResultingRoleAssignmentId",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleAssignments_Users_RevokedByUserId",
                schema: "identity",
                table: "RoleAssignments");

            migrationBuilder.DropIndex(
                name: "IX_RoleAssignments_RevokedByUserId",
                schema: "identity",
                table: "RoleAssignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RoleAssignments_Revocation",
                schema: "identity",
                table: "RoleAssignments");

            migrationBuilder.DropIndex(
                name: "IX_AccessGrantRequests_CompanyId_ResultingRoleAssignmentId",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropIndex(
                name: "UX_AccessGrantRequests_Pending",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropIndex(
                name: "UX_AccessGrantRequests_ResultingRoleAssignment",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_DecisionReason",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_DecisionState",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_ResultingAssignment",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_Validity",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                schema: "identity",
                table: "RoleAssignments");

            migrationBuilder.DropColumn(
                name: "RevokedByUserId",
                schema: "identity",
                table: "RoleAssignments");

            migrationBuilder.DropColumn(
                name: "ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropColumn(
                name: "DecisionReasonCode",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropColumn(
                name: "ResultingRoleAssignmentId",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropColumn(
                name: "ValidFromUtc",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropColumn(
                name: "ValidUntilUtc",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_DecisionState",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "([Status] = N'Pending' AND [DecidedByUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR ([Status] IN (N'Approved', N'Rejected') AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[RequiresSecondApproval] = 0 OR [DecidedByUserId] IS NULL OR ([DecidedByUserId] <> [RequestedByUserId] AND [DecidedByUserId] <> [TargetUserId])");
        }
    }
}
