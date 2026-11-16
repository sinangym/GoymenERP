using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpeningBalances",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetKind = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: true),
                    AmountText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OpeningPeriodVersion = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProposedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalances", x => x.Id);
                    table.UniqueConstraint("AK_OpeningBalances_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_OpeningBalances_Amount", "LEN([AmountText]) > 0 AND [AmountText] NOT LIKE '%[^0-9.]%' AND [AmountText] NOT LIKE '%.%.%' AND [AmountText] NOT LIKE '.%' AND [AmountText] NOT LIKE '%.' AND [AmountText] LIKE '%[1-9]%'");
                    table.CheckConstraint("CK_OpeningBalances_Decision", "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0)");
                    table.CheckConstraint("CK_OpeningBalances_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [TargetId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ProposedBy]) > 0 AND LEN([OpeningPeriodVersion]) > 0");
                    table.CheckConstraint("CK_OpeningBalances_Target", "([TargetKind] IN (1, 2) AND [PartyId] IS NULL AND [Direction] IS NULL) OR ([TargetKind] = 3 AND [PartyId] IS NOT NULL AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [Direction] IS NOT NULL AND [Direction] IN (1, 2))");
                    table.CheckConstraint("CK_OpeningBalances_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_OpeningBalances_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceCommands",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OpeningBalanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceCommands", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_OpeningBalanceCommands_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ActorId]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_OpeningBalanceCommands_KeyHash", "LEN([IdempotencyKeyHash]) = 64");
                    table.CheckConstraint("CK_OpeningBalanceCommands_PayloadHash", "LEN([PayloadHash]) = 64");
                    table.CheckConstraint("CK_OpeningBalanceCommands_ResultJson", "ISJSON([ResultJson]) = 1");
                    table.CheckConstraint("CK_OpeningBalanceCommands_Target", "([OpeningBalanceId] IS NOT NULL AND [OpeningBalanceId] <> '00000000-0000-0000-0000-000000000000') OR ([OpeningBalanceId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')");
                    table.ForeignKey(
                        name: "FK_OpeningBalanceCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceCommands_OpeningBalances_CompanyId_OpeningBalanceId",
                        columns: x => new { x.CompanyId, x.OpeningBalanceId },
                        principalSchema: "finance",
                        principalTable: "OpeningBalances",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceCommands_CompanyId_OpeningBalanceId",
                schema: "finance",
                table: "OpeningBalanceCommands",
                columns: new[] { "CompanyId", "OpeningBalanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalances_Status",
                schema: "finance",
                table: "OpeningBalances",
                columns: new[] { "CompanyId", "Status", "ProposedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_OpeningBalances_Live",
                schema: "finance",
                table: "OpeningBalances",
                columns: new[] { "CompanyId", "TargetKind", "TargetId", "CurrencyId" },
                unique: true,
                filter: "[Status] IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[OpeningBalances])
                    OR EXISTS (SELECT 1 FROM [finance].[OpeningBalanceCommands])
                BEGIN
                    THROW 51809, 'Opening balance records exist; AddOpeningBalances is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OpeningBalanceCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "OpeningBalances",
                schema: "finance");
        }
    }
}
