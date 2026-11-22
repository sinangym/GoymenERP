using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankStatementMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementMatches",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateKind = table.Column<int>(type: "int", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CounterpartyCheck = table.Column<int>(type: "int", nullable: false),
                    ConfirmedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReversalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementMatches", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_BankStatementMatches_Content", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ImportId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [BankAccountId] <> '00000000-0000-0000-0000-000000000000' AND [RecordId] <> '00000000-0000-0000-0000-000000000000' AND [CandidateKind] IN (1,2,3,4) AND [CounterpartyCheck] IN (1,2) AND LEN([ConfirmedBy]) > 0 AND (([ReversalId] IS NULL AND [Version] = 1) OR ([ReversalId] IS NOT NULL AND [ReversalId] <> '00000000-0000-0000-0000-000000000000' AND [Version] = 2))");
                    table.ForeignKey(
                        name: "FK_BankStatementMatches_BankStatementImports_CompanyId_ImportId",
                        columns: x => new { x.CompanyId, x.ImportId },
                        principalSchema: "finance",
                        principalTable: "BankStatementImports",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementMatches_BankStatementLines_CompanyId_LineId",
                        columns: x => new { x.CompanyId, x.LineId },
                        principalSchema: "finance",
                        principalTable: "BankStatementLines",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementMatchReversals",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ReversedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementMatchReversals", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_BankStatementMatchReversals_Content", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [MatchId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Reason]) > 0 AND LEN([ReversedBy]) > 0");
                    table.ForeignKey(
                        name: "FK_BankStatementMatchReversals_BankStatementMatches_CompanyId_MatchId",
                        columns: x => new { x.CompanyId, x.MatchId },
                        principalSchema: "finance",
                        principalTable: "BankStatementMatches",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementMatches_CompanyId_ImportId",
                schema: "finance",
                table: "BankStatementMatches",
                columns: new[] { "CompanyId", "ImportId" });

            migrationBuilder.CreateIndex(
                name: "UX_BankStatementMatches_ActiveLine",
                schema: "finance",
                table: "BankStatementMatches",
                columns: new[] { "CompanyId", "LineId" },
                unique: true,
                filter: "[ReversalId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_BankStatementMatches_ActiveRecord",
                schema: "finance",
                table: "BankStatementMatches",
                columns: new[] { "CompanyId", "CandidateKind", "RecordId" },
                unique: true,
                filter: "[ReversalId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementMatchReversals_CompanyId_MatchId",
                schema: "finance",
                table: "BankStatementMatchReversals",
                columns: new[] { "CompanyId", "MatchId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // FNS-D113 forward fix: a confirmed match and its reversal record are the person's reconciliation decision; dropping
            // them would lose it. An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[BankStatementMatches])
                   OR EXISTS (SELECT 1 FROM [finance].[BankStatementMatchReversals])
                BEGIN
                    THROW 52251, 'Bank statement matches exist; AddBankStatementMatches is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "BankStatementMatchReversals",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "BankStatementMatches",
                schema: "finance");
        }
    }
}
