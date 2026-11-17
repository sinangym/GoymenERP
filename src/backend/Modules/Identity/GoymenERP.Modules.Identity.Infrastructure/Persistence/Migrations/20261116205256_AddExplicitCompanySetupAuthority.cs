using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExplicitCompanySetupAuthority : Migration
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

            migrationBuilder.CreateTable(
                name: "CompanySetupGroupAssignments",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RevocationDecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevocationDecisionReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true, collation: "Latin1_General_100_BIN2"),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySetupGroupAssignments", x => x.Id);
                    table.CheckConstraint("CK_CompanySetupGroup_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [UserId] <> '00000000-0000-0000-0000-000000000000' AND [DecisionId] <> '00000000-0000-0000-0000-000000000000' AND LEN([DecisionReference]) > 0");
                    table.CheckConstraint("CK_CompanySetupGroup_Operation", "[Operation] IN (1, 2)");
                    table.CheckConstraint("CK_CompanySetupGroup_Revocation", "([IsActive] = 1 AND [RevocationDecisionId] IS NULL AND [RevocationDecisionReference] IS NULL AND [RevokedAtUtc] IS NULL) OR ([IsActive] = 0 AND [RevocationDecisionId] IS NOT NULL AND [RevocationDecisionId] <> [DecisionId] AND [RevocationDecisionId] <> '00000000-0000-0000-0000-000000000000' AND [RevocationDecisionReference] IS NOT NULL AND LEN([RevocationDecisionReference]) > 0 AND [RevokedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET, [RevokedAtUtc]) = 0)");
                    table.CheckConstraint("CK_CompanySetupGroup_Validity", "DATEPART(TZOFFSET, [ValidFromUtc]) = 0 AND ([ValidUntilUtc] IS NULL OR ([ValidUntilUtc] > [ValidFromUtc] AND DATEPART(TZOFFSET, [ValidUntilUtc]) = 0))");
                    table.ForeignKey(
                        name: "FK_CompanySetupGroupAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_CompanySetupGroupAssignments_DecisionId",
                schema: "identity",
                table: "CompanySetupGroupAssignments",
                column: "DecisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySetupGroupAssignments_UserId_Operation",
                schema: "identity",
                table: "CompanySetupGroupAssignments",
                columns: new[] { "UserId", "Operation" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[CompanySetupGroupAssignments]) OR EXISTS (SELECT 1 FROM [identity].[AccessGrantRequests] WHERE [ApprovalMode] = N'CompanySetupAuthority')
                BEGIN
                    THROW 51947, 'AddExplicitCompanySetupAuthority history exists; rollback is refused (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CompanySetupGroupAssignments",
                schema: "identity");

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
                sql: "([Status] = N'Approved' AND [ApprovalMode] IN (N'SecondApproval', N'FounderDirect', N'CompanyOpener')) OR ([Status] <> N'Approved' AND [ApprovalMode] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessGrantRequests_SecondApproval",
                schema: "identity",
                table: "AccessGrantRequests",
                sql: "[RequiresSecondApproval] = 0 OR [DecidedByUserId] IS NULL OR ([Status] = N'Withdrawn' AND [DecidedByUserId] = [RequestedByUserId]) OR ([Status] = N'Approved' AND [ApprovalMode] IN (N'FounderDirect', N'CompanyOpener') AND [DecidedByUserId] = [RequestedByUserId]) OR ([DecidedByUserId] <> [RequestedByUserId] AND [DecidedByUserId] <> [TargetUserId])");
        }
    }
}
