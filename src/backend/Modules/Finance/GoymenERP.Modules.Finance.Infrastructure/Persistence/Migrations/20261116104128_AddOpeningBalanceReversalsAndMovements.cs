using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningBalanceReversalsAndMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OpeningBalances_Decision",
                schema: "finance",
                table: "OpeningBalances");

            migrationBuilder.CreateTable(
                name: "OpeningBalanceMovements",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpeningBalanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TargetKind = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: true),
                    AmountText = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningBalanceMovements", x => x.Id);
                    table.CheckConstraint("CK_OpeningBalanceMovements_Amount", "LEN([AmountText]) > 0 AND [AmountText] NOT LIKE '%[^0-9.]%' AND [AmountText] NOT LIKE '%.%.%' AND [AmountText] NOT LIKE '.%' AND [AmountText] NOT LIKE '%.' AND [AmountText] LIKE '%[1-9]%'");
                    table.CheckConstraint("CK_OpeningBalanceMovements_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OpeningBalanceId] <> '00000000-0000-0000-0000-000000000000' AND [TargetId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([RecordedBy]) > 0");
                    table.CheckConstraint("CK_OpeningBalanceMovements_Kind", "[Kind] IN (1, 2)");
                    table.CheckConstraint("CK_OpeningBalanceMovements_Target", "([TargetKind] IN (1, 2) AND [PartyId] IS NULL AND [Direction] IS NULL) OR ([TargetKind] = 3 AND [PartyId] IS NOT NULL AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [Direction] IS NOT NULL AND [Direction] IN (1, 2))");
                    table.ForeignKey(
                        name: "FK_OpeningBalanceMovements_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceMovements_OpeningBalances_CompanyId_OpeningBalanceId",
                        columns: x => new { x.CompanyId, x.OpeningBalanceId },
                        principalSchema: "finance",
                        principalTable: "OpeningBalances",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningBalanceReversals",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpeningBalanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_OpeningBalanceReversals", x => x.Id);
                    table.CheckConstraint("CK_OpeningBalanceReversals_Decision", "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0)");
                    table.CheckConstraint("CK_OpeningBalanceReversals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [OpeningBalanceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([ProposedBy]) > 0 AND LEN([OpeningPeriodVersion]) > 0 AND LEN([Reason]) > 0");
                    table.CheckConstraint("CK_OpeningBalanceReversals_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_OpeningBalanceReversals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpeningBalanceReversals_OpeningBalances_CompanyId_OpeningBalanceId",
                        columns: x => new { x.CompanyId, x.OpeningBalanceId },
                        principalSchema: "finance",
                        principalTable: "OpeningBalances",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OpeningBalances_Decision",
                schema: "finance",
                table: "OpeningBalances",
                sql: "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] IN (2, 4) AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceMovements_Party",
                schema: "finance",
                table: "OpeningBalanceMovements",
                columns: new[] { "CompanyId", "PartyId", "TargetId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "UX_OpeningBalanceMovements_Kind",
                schema: "finance",
                table: "OpeningBalanceMovements",
                columns: new[] { "CompanyId", "OpeningBalanceId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OpeningBalanceReversals_Live",
                schema: "finance",
                table: "OpeningBalanceReversals",
                columns: new[] { "CompanyId", "OpeningBalanceId" },
                unique: true,
                filter: "[Status] IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[OpeningBalanceReversals])
                    OR EXISTS (SELECT 1 FROM [finance].[OpeningBalanceMovements])
                    OR EXISTS (SELECT 1 FROM [finance].[OpeningBalances] WHERE [Status] = 4)
                BEGIN
                    THROW 51841, 'Opening balance movements or reversals exist; AddOpeningBalanceReversalsAndMovements is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OpeningBalanceMovements",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "OpeningBalanceReversals",
                schema: "finance");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OpeningBalances_Decision",
                schema: "finance",
                table: "OpeningBalances");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OpeningBalances_Decision",
                schema: "finance",
                table: "OpeningBalances",
                sql: "([Status] = 1 AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) OR ([Status] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NULL) OR ([Status] = 3 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0)");
        }
    }
}
