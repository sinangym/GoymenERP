using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalTwoFactorSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalCorrelationId",
                schema: "identity",
                table: "LocalCredentialCommands",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LocalTwoFactorSecrets",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProtectedSecret = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RecoveryHashesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastAcceptedStep = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalTwoFactorSecrets", x => x.UserId);
                    table.CheckConstraint("CK_LocalTwoFactorSecrets_State", "[UserId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND ([LastAcceptedStep] IS NULL OR [LastAcceptedStep] >= 0) AND ISJSON([RecoveryHashesJson]) = 1");
                    table.ForeignKey(
                        name: "FK_LocalTwoFactorSecrets_LocalCredentials_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "LocalCredentials",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[LocalTwoFactorSecrets])
                    OR EXISTS (SELECT 1 FROM [identity].[LocalCredentials] WHERE [TwoFactorEnabled] = 1)
                    OR EXISTS (SELECT 1 FROM [identity].[LocalCredentialCommands] WHERE [OriginalCorrelationId] IS NOT NULL)
                BEGIN
                    THROW 51967, 'Two-factor credentials or command evidence exist; AddLocalTwoFactorSecrets is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "LocalTwoFactorSecrets",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "OriginalCorrelationId",
                schema: "identity",
                table: "LocalCredentialCommands");
        }
    }
}
