using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashExchangeBookValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashExchangeBookValueCommands",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Command = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    KeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashExchangeBookValueCommands", x => new { x.CompanyId, x.Command, x.KeyHash });
                    table.CheckConstraint("CK_CashExchangeBookValueCommands_Receipt", "LEN([KeyHash]) = 64 AND LEN([PayloadHash]) = 64 AND LEN([ActorId]) > 0 AND ISJSON([ResultJson]) = 1");
                    table.ForeignKey(
                        name: "FK_CashExchangeBookValueCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashExchangeBookValueProposals",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferSourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TransferSourceHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PreparationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashExchangeBookValueProposals", x => new { x.CompanyId, x.Id });
                    table.UniqueConstraint("AK_CashExchangeBookValueProposals_CompanyId_Id_TransferId_PreparedBy", x => new { x.CompanyId, x.Id, x.TransferId, x.PreparedBy });
                    table.CheckConstraint("CK_CashExchangeBookValueProposals_Proof", "[Id] <> '00000000-0000-0000-0000-000000000000' AND ISJSON([TransferSourceJson]) = 1 AND LEN([TransferSourceHash]) = 64 AND ISJSON([PreparationJson]) = 1 AND LEN([PreparedBy]) > 0");
                    table.ForeignKey(
                        name: "FK_CashExchangeBookValueProposals_CashTransfers_CompanyId_TransferId",
                        columns: x => new { x.CompanyId, x.TransferId },
                        principalSchema: "finance",
                        principalTable: "CashTransfers",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashExchangeBookValueDecisions",
                schema: "finance",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Approved = table.Column<bool>(type: "bit", nullable: false),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashExchangeBookValueDecisions", x => new { x.CompanyId, x.ProposalId });
                    table.CheckConstraint("CK_CashExchangeBookValueDecisions_Checker", "LEN([PreparedBy]) > 0 AND LEN([DecidedBy]) > 0 AND [PreparedBy] <> [DecidedBy] AND (([Approved] = 1 AND [Reason] IS NULL) OR ([Approved] = 0 AND LEN([Reason]) > 0))");
                    table.ForeignKey(
                        name: "FK_CashExchangeBookValueDecisions_CashExchangeBookValueProposals_CompanyId_ProposalId_TransferId_PreparedBy",
                        columns: x => new { x.CompanyId, x.ProposalId, x.TransferId, x.PreparedBy },
                        principalSchema: "finance",
                        principalTable: "CashExchangeBookValueProposals",
                        principalColumns: new[] { "CompanyId", "Id", "TransferId", "PreparedBy" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashExchangeBookValueDecisions_CompanyId_ProposalId_TransferId_PreparedBy",
                schema: "finance",
                table: "CashExchangeBookValueDecisions",
                columns: new[] { "CompanyId", "ProposalId", "TransferId", "PreparedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_CashExchangeBookValueDecisions_CompanyId_TransferId",
                schema: "finance",
                table: "CashExchangeBookValueDecisions",
                columns: new[] { "CompanyId", "TransferId" },
                unique: true,
                filter: "[Approved] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CashExchangeBookValueProposals_CompanyId_TransferId",
                schema: "finance",
                table: "CashExchangeBookValueProposals",
                columns: new[] { "CompanyId", "TransferId" });

            // Immutable evidence, decisions and original command receipts; no mutable status row.
            migrationBuilder.Sql("""
                CREATE TRIGGER [finance].[TR_CashExchangeBookValueProposals_Immutable]
                ON [finance].[CashExchangeBookValueProposals] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51912, 'Cash exchange book-value proposals are immutable.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [finance].[TR_CashExchangeBookValueDecisions_Immutable]
                ON [finance].[CashExchangeBookValueDecisions] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51913, 'Cash exchange book-value decisions are immutable.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [finance].[TR_CashExchangeBookValueCommands_Immutable]
                ON [finance].[CashExchangeBookValueCommands] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51914, 'Cash exchange book-value command receipts are immutable.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[CashExchangeBookValueProposals])
                    OR EXISTS (SELECT 1 FROM [finance].[CashExchangeBookValueDecisions])
                    OR EXISTS (SELECT 1 FROM [finance].[CashExchangeBookValueCommands])
                    THROW 51915, 'Cash exchange book-value evidence exists. Use a forward fix.', 1;
                """);

            migrationBuilder.DropTable(
                name: "CashExchangeBookValueCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CashExchangeBookValueDecisions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CashExchangeBookValueProposals",
                schema: "finance");
        }
    }
}
