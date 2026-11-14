using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyCreditLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartyCreditLimits",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AmountText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsDirect = table.Column<bool>(type: "bit", nullable: false),
                    BaseRevision = table.Column<long>(type: "bigint", nullable: false),
                    AccountRevision = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProposedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SupersededAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyCreditLimits", x => x.Id);
                    table.UniqueConstraint("AK_PartyCreditLimits_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PartyCreditLimits_Decision", "([Status] = 1 AND [IsDirect] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [AccountRevision] IS NULL AND [SupersededAtUtc] IS NULL) OR ([Status] IN (2, 4) AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [AccountRevision] IS NOT NULL AND [RejectionReason] IS NULL AND (([Status] = 2 AND [SupersededAtUtc] IS NULL) OR ([Status] = 4 AND [SupersededAtUtc] IS NOT NULL)) AND (([IsDirect] = 1 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 = [ProposedBy] COLLATE Latin1_General_100_BIN2) OR ([IsDirect] = 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2))) OR ([Status] = 3 AND [IsDirect] = 0 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND [AccountRevision] IS NULL AND [SupersededAtUtc] IS NULL) OR ([Status] = 4 AND [IsDirect] = 0 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [AccountRevision] IS NULL AND [SupersededAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_PartyCreditLimits_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ProposedBy]) > 0");
                    table.CheckConstraint("CK_PartyCreditLimits_Payload", "([Kind] = 1 AND [CurrencyId] IS NOT NULL AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [AmountText] IS NOT NULL AND LEN([AmountText]) > 0 AND [AmountText] NOT LIKE '%[^0-9.]%' AND [AmountText] NOT LIKE '%.%.%' AND [AmountText] NOT LIKE '.%' AND [AmountText] NOT LIKE '%.' AND [AmountText] LIKE '%[1-9]%') OR ([Kind] = 2 AND [CurrencyId] IS NULL AND [AmountText] IS NULL)");
                    table.CheckConstraint("CK_PartyCreditLimits_Revision", "[Version] >= 1 AND [BaseRevision] >= 0 AND ([AccountRevision] IS NULL OR [AccountRevision] > [BaseRevision])");
                    table.ForeignKey(
                        name: "FK_PartyCreditLimits_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartyCreditLimitCommands",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PartyCreditLimitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyCreditLimitCommands", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_PartyCreditLimitCommands_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ActorId]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PartyCreditLimitCommands_KeyHash", "LEN([IdempotencyKeyHash]) = 64");
                    table.CheckConstraint("CK_PartyCreditLimitCommands_PayloadHash", "LEN([PayloadHash]) = 64");
                    table.CheckConstraint("CK_PartyCreditLimitCommands_ResultJson", "ISJSON([ResultJson]) = 1");
                    table.CheckConstraint("CK_PartyCreditLimitCommands_Target", "([PartyCreditLimitId] IS NOT NULL AND [PartyCreditLimitId] <> '00000000-0000-0000-0000-000000000000') OR ([PartyCreditLimitId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') IN ('ValidationFailed', 'VersionConflict'))");
                    table.ForeignKey(
                        name: "FK_PartyCreditLimitCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartyCreditLimitCommands_PartyCreditLimits_CompanyId_PartyCreditLimitId",
                        columns: x => new { x.CompanyId, x.PartyCreditLimitId },
                        principalSchema: "finance",
                        principalTable: "PartyCreditLimits",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartyCreditLimitCommands_CompanyId_PartyCreditLimitId",
                schema: "finance",
                table: "PartyCreditLimitCommands",
                columns: new[] { "CompanyId", "PartyCreditLimitId" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyCreditLimits_CompanyId_Status",
                schema: "finance",
                table: "PartyCreditLimits",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PartyCreditLimits_History",
                schema: "finance",
                table: "PartyCreditLimits",
                columns: new[] { "CompanyId", "CompanyAccountId", "ProposedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_PartyCreditLimits_Live",
                schema: "finance",
                table: "PartyCreditLimits",
                columns: new[] { "CompanyId", "CompanyAccountId", "Status" },
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_PartyCreditLimits_Revision",
                schema: "finance",
                table: "PartyCreditLimits",
                columns: new[] { "CompanyId", "CompanyAccountId", "AccountRevision" },
                unique: true,
                filter: "[AccountRevision] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a stored limit, proposal, decision or recorded attempt is the effective credit limit history
            // (FNS-D27/D27a); dropping it would silently erase who set which limit. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[PartyCreditLimits])
                    OR EXISTS (SELECT 1 FROM [finance].[PartyCreditLimitCommands])
                BEGIN
                    THROW 51274, 'Party credit limit records exist; AddPartyCreditLimits is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "PartyCreditLimitCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "PartyCreditLimits",
                schema: "finance");
        }
    }
}
