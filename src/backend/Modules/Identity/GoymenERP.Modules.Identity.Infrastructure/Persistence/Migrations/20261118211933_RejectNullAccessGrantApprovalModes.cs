using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RejectNullAccessGrantApprovalModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "([Status] = N'Approved' AND [ApprovalMode] IS NOT NULL AND [ApprovalMode] IN (N'SecondApproval', N'FounderDirect', N'CompanyOpener', N'CompanySetupAuthority')) OR ([Status] <> N'Approved' AND [ApprovalMode] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[RequiresSecondApproval] = 0 OR [DecidedByUserId] IS NULL OR ([Status] = N'Withdrawn' AND [DecidedByUserId] = [RequestedByUserId]) OR ([Status] = N'Approved' AND [ApprovalMode] IS NOT NULL AND [ApprovalMode] IN (N'FounderDirect', N'CompanyOpener', N'CompanySetupAuthority') AND [DecidedByUserId] = [RequestedByUserId]) OR ([DecidedByUserId] <> [RequestedByUserId] AND [DecidedByUserId] <> [TargetUserId])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[AccessGrantRequests] WHERE [Status] = N'Approved')
                BEGIN
                    THROW 51798, 'Approved access grant history exists; RejectNullAccessGrantApprovalModes requires a forward fix.', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "([Status] = N'Approved' AND [ApprovalMode] IN (N'SecondApproval', N'FounderDirect', N'CompanyOpener', N'CompanySetupAuthority')) OR ([Status] <> N'Approved' AND [ApprovalMode] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[RequiresSecondApproval] = 0 OR [DecidedByUserId] IS NULL OR ([Status] = N'Withdrawn' AND [DecidedByUserId] = [RequestedByUserId]) OR ([Status] = N'Approved' AND [ApprovalMode] IN (N'FounderDirect', N'CompanyOpener', N'CompanySetupAuthority') AND [DecidedByUserId] = [RequestedByUserId]) OR ([DecidedByUserId] <> [RequestedByUserId] AND [DecidedByUserId] <> [TargetUserId])");
        }
    }
}
