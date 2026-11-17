using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditActualMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreditMovementCommands",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Command = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditMovementCommands", x => new { x.CompanyId, x.Command, x.KeyHash });
                    table.CheckConstraint("CK_CreditMovementCommands_Json", "ISJSON([ResultJson]) = 1 AND LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64");
                });

            migrationBuilder.CreateTable(
                name: "CreditMovements",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceIdentityHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    MoneyProfileJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OpeningPeriodVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BankAmountText = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    ReplacesOpeningMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversedByMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProposedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditMovements", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_CreditMovements_Bank", "([BankAccountId] IS NULL AND [BankAmountText] IS NULL) OR ([BankAccountId] IS NOT NULL AND [BankAmountText] IS NOT NULL AND [Kind] IN (1,2))");
                    table.CheckConstraint("CK_CreditMovements_Decision", "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [Version] = 1) OR ([Status] IN (2,3) AND [DecidedBy] IS NOT NULL AND [DecidedBy] <> [ProposedBy] AND [DecidedAtUtc] IS NOT NULL AND [Version] >= 2 AND (([Status] = 2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND LEN([RejectionReason]) > 0)))");
                    table.CheckConstraint("CK_CreditMovements_Kind", "[Kind] IN (1,2,3,4)");
                    table.CheckConstraint("CK_CreditMovements_Opening", "([Kind] = 4 AND [BankAccountId] IS NULL) OR ([Kind] <> 4 AND [ReplacesOpeningMovementId] IS NULL AND [OpeningPeriodVersion] IS NULL AND [ReversedByMovementId] IS NULL)");
                    table.CheckConstraint("CK_CreditMovements_Source", "ISJSON([SourceJson]) = 1 AND ISJSON([MoneyProfileJson]) = 1 AND LEN([SourceIdentityHash]) = 64");
                    table.ForeignKey(
                        name: "FK_CreditMovements_BankAccounts_CompanyId_BankAccountId",
                        columns: x => new { x.CompanyId, x.BankAccountId },
                        principalSchema: "finance",
                        principalTable: "BankAccounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditMovements_CreditAccounts_CompanyId_CreditAccountId",
                        columns: x => new { x.CompanyId, x.CreditAccountId },
                        principalSchema: "finance",
                        principalTable: "CreditAccounts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditMovements_CreditMovements_CompanyId_ReplacesOpeningMovementId",
                        columns: x => new { x.CompanyId, x.ReplacesOpeningMovementId },
                        principalSchema: "finance",
                        principalTable: "CreditMovements",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_OutboxMessages_Source", "[SchemaVersion] > 0 AND [AggregateVersion] > 0 AND ISJSON([PayloadJson]) = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditMovements_CompanyId_BankAccountId_Status",
                schema: "finance",
                table: "CreditMovements",
                columns: new[] { "CompanyId", "BankAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditMovements_CompanyId_CreditAccountId",
                schema: "finance",
                table: "CreditMovements",
                columns: new[] { "CompanyId", "CreditAccountId" },
                unique: true,
                filter: "[Kind] = 4 AND [ReplacesOpeningMovementId] IS NULL AND [Status] IN (1,2)");

            migrationBuilder.CreateIndex(
                name: "IX_CreditMovements_CompanyId_CreditAccountId_SourceIdentityHash",
                schema: "finance",
                table: "CreditMovements",
                columns: new[] { "CompanyId", "CreditAccountId", "SourceIdentityHash" },
                unique: true,
                filter: "[Status] IN (1,2)");

            migrationBuilder.CreateIndex(
                name: "IX_CreditMovements_CompanyId_ReplacesOpeningMovementId",
                schema: "finance",
                table: "CreditMovements",
                columns: new[] { "CompanyId", "ReplacesOpeningMovementId" },
                unique: true,
                filter: "[ReplacesOpeningMovementId] IS NOT NULL AND [Status] IN (1,2)");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CompanyId_EventType_AggregateId_AggregateVersion",
                schema: "finance",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "EventType", "AggregateId", "AggregateVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Position",
                schema: "finance",
                table: "OutboxMessages",
                column: "Position");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[CreditMovements])
                    OR EXISTS (SELECT 1 FROM [finance].[CreditMovementCommands])
                    OR EXISTS (SELECT 1 FROM [finance].[OutboxMessages])
                    THROW 51969, 'Credit actual source, command receipt or outbox event exists; use a forward fix.', 1;
                """);
            migrationBuilder.DropTable(
                name: "CreditMovementCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CreditMovements",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "finance");
        }
    }
}
