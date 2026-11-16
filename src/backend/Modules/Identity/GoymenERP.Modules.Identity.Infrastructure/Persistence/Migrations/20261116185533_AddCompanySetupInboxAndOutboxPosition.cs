using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanySetupInboxAndOutboxPosition : Migration
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

            migrationBuilder.AddColumn<byte[]>(
                name: "Position",
                schema: "identity",
                table: "OutboxMessages",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "CompanySetupInboxReceipts",
                schema: "identity",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySetupInboxReceipts", x => new { x.CompanyId, x.EventId });
                    table.CheckConstraint("CK_CompanySetupInbox_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [EventId] <> '00000000-0000-0000-0000-000000000000' AND LEN([PayloadHash]) = 64 AND [PayloadHash] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9A-F]%'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CompanyId_Position",
                schema: "identity",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "Position" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "([Status] = N'Approved' AND [ApprovalMode] IN (N'SecondApproval', N'FounderDirect', N'CompanyOpener')) OR ([Status] <> N'Approved' AND [ApprovalMode] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[RequiresSecondApproval] = 0 OR [DecidedByUserId] IS NULL OR ([Status] = N'Withdrawn' AND [DecidedByUserId] = [RequestedByUserId]) OR ([Status] = N'Approved' AND [ApprovalMode] IN (N'FounderDirect', N'CompanyOpener') AND [DecidedByUserId] = [RequestedByUserId]) OR ([DecidedByUserId] <> [RequestedByUserId] AND [DecidedByUserId] <> [TargetUserId])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[CompanySetupInboxReceipts])
                    OR EXISTS (SELECT 1 FROM [identity].[AccessGrantRequests] WHERE [ApprovalMode] = N'CompanyOpener')
                    OR EXISTS (SELECT 1 FROM [identity].[OutboxMessages] WHERE [EventType] = N'identity.company.first_admin_assigned.v1')
                BEGIN
                    THROW 51944, 'Company setup history exists; AddCompanySetupInboxAndOutboxPosition is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CompanySetupInboxReceipts",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CompanyId_Position",
                schema: "identity",
                table: "OutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests");

            migrationBuilder.DropColumn(
                name: "Position",
                schema: "identity",
                table: "OutboxMessages");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_ApprovalMode",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "([Status] = N'Approved' AND [ApprovalMode] IN (N'SecondApproval', N'FounderDirect')) OR ([Status] <> N'Approved' AND [ApprovalMode] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[RequiresSecondApproval] = 0 OR [DecidedByUserId] IS NULL OR ([Status] = N'Withdrawn' AND [DecidedByUserId] = [RequestedByUserId]) OR ([Status] = N'Approved' AND [ApprovalMode] = N'FounderDirect' AND [DecidedByUserId] = [RequestedByUserId]) OR ([DecidedByUserId] <> [RequestedByUserId] AND [DecidedByUserId] <> [TargetUserId])");
        }
    }
}
