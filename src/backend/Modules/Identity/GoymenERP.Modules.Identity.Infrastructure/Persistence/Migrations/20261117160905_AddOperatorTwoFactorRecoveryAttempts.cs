using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorTwoFactorRecoveryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocalTwoFactorOperatorRecoveryAttempts",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    TargetInputHash = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperatorHash = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ConfirmedUserVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedCredentialVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedSecretVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmedTwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ResultCredentialVersion = table.Column<long>(type: "bigint", nullable: false),
                    ResultSecretVersion = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OriginalCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalTwoFactorOperatorRecoveryAttempts", x => x.Id);
                    table.CheckConstraint("CK_LocalTwoFactorOperatorRecoveryAttempts_State", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [TargetUserId] <> '00000000-0000-0000-0000-000000000000' AND [ConfirmedUserVersion] >= 1 AND [ConfirmedCredentialVersion] >= 1 AND [ConfirmedSecretVersion] >= 0 AND [ResultCredentialVersion] >= [ConfirmedCredentialVersion] AND [ResultSecretVersion] >= 0 AND [Outcome] IN (1,2,3) AND LEN([Reason]) BETWEEN 1 AND 1000 AND LEN([OriginalCorrelationId]) BETWEEN 1 AND 128 AND LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN([TargetInputHash]) = 64 AND LEN([OperatorHash]) = 64 AND DATEPART(TZOFFSET, [RecordedAtUtc]) = 0 AND [KeyHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%' AND [RequestHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%' AND [TargetInputHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%' AND [OperatorHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%' AND (([Outcome] = 1 AND [ConfirmedTwoFactorEnabled] = 1 AND [ConfirmedSecretVersion] >= 1 AND CONVERT(decimal(20,0),[ResultCredentialVersion]) - CONVERT(decimal(20,0),[ConfirmedCredentialVersion]) = 1 AND CONVERT(decimal(20,0),[ResultSecretVersion]) - CONVERT(decimal(20,0),[ConfirmedSecretVersion]) = 1) OR ([Outcome] = 2 AND [ConfirmedTwoFactorEnabled] = 0 AND [ResultCredentialVersion] = [ConfirmedCredentialVersion] AND [ResultSecretVersion] = [ConfirmedSecretVersion]) OR [Outcome] = 3)");
                    table.ForeignKey(
                        name: "FK_LocalTwoFactorOperatorRecoveryAttempts_LocalCredentials_TargetUserId",
                        column: x => x.TargetUserId,
                        principalSchema: "identity",
                        principalTable: "LocalCredentials",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocalTwoFactorOperatorRecoveryAttempts_TargetUserId_RecordedAtUtc_Id",
                schema: "identity",
                table: "LocalTwoFactorOperatorRecoveryAttempts",
                columns: new[] { "TargetUserId", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_LocalTwoFactorOperatorRecoveryAttempts_KeyHash",
                schema: "identity",
                table: "LocalTwoFactorOperatorRecoveryAttempts",
                column: "KeyHash",
                unique: true);

            // Inserts remain ordinary EF inserts. The durable command history cannot be rewritten or removed.
            migrationBuilder.Sql("""
                CREATE TRIGGER [identity].[TR_LocalTwoFactorOperatorRecoveryAttempts_Immutable]
                ON [identity].[LocalTwoFactorOperatorRecoveryAttempts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    IF (ROWCOUNT_BIG() = 0) RETURN;
                    SET NOCOUNT ON;
                    THROW 51925, 'Operator two-factor recovery history is immutable; append a new confirmed command.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[LocalTwoFactorOperatorRecoveryAttempts])
                BEGIN
                    THROW 51922, 'Operator two-factor recovery history exists; AddOperatorTwoFactorRecoveryAttempts requires a forward fix.', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "LocalTwoFactorOperatorRecoveryAttempts",
                schema: "identity");
        }
    }
}
