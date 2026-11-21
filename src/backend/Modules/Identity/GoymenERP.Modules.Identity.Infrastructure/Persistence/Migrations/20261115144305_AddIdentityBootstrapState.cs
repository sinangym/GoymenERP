using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityBootstrapState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentityBootstrapState",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "tinyint", nullable: false),
                    FounderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityBootstrapState", x => x.Id);
                    table.CheckConstraint("CK_IdentityBootstrapState_Singleton", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_IdentityBootstrapState_Users_FounderUserId",
                        column: x => x.FounderUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocalCredentialCommands",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestFingerprint = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    ResultVersion = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalCredentialCommands", x => x.Id);
                    table.CheckConstraint("CK_LocalCredentialCommands_Shape", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ResultVersion] >= 1 AND DATALENGTH([RequestFingerprint]) = 32 AND LEN([IdempotencyKey]) > 0");
                    table.ForeignKey(
                        name: "FK_LocalCredentialCommands_LocalCredentials_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "LocalCredentials",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    SourceOperation = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AggregateType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_IdentityOutbox_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [AggregateId] <> '00000000-0000-0000-0000-000000000000' AND ([CompanyId] IS NULL OR [CompanyId] <> '00000000-0000-0000-0000-000000000000') AND [SchemaVersion] >= 1 AND [AggregateVersion] >= 1 AND LEN([EventType]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_IdentityOutbox_Payload", "ISJSON([PayloadJson]) = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityBootstrapState_FounderUserId",
                schema: "identity",
                table: "IdentityBootstrapState",
                column: "FounderUserId");

            migrationBuilder.CreateIndex(
                name: "UX_LocalCredentialCommands_UserOperationKey",
                schema: "identity",
                table: "LocalCredentialCommands",
                columns: new[] { "UserId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentityOutbox_Recorded",
                schema: "identity",
                table: "OutboxMessages",
                columns: new[] { "RecordedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: the bootstrap marker, the identity outbox and the credential command journal are evidence
            // (one-shot founder bootstrap, undispatched events, idempotency records). While any row exists the
            // migration is not rolled back; an empty store still rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [identity].[IdentityBootstrapState])
                    OR EXISTS (SELECT 1 FROM [identity].[OutboxMessages])
                    OR EXISTS (SELECT 1 FROM [identity].[LocalCredentialCommands])
                BEGIN
                    THROW 51399, 'Identity bootstrap state, outbox or credential command rows exist; AddIdentityBootstrapState is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "IdentityBootstrapState",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "LocalCredentialCommands",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "identity");
        }
    }
}
