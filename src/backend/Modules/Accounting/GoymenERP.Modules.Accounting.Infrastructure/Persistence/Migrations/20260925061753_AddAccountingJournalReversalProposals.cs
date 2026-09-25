using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingJournalReversalProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JournalReversalProposals",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReversalJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AccountingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    ProposedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalReversalProposals", x => x.Id);
                    table.UniqueConstraint("AK_JournalReversalProposals_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_JournalReversalProposals_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceJournalId] <> '00000000-0000-0000-0000-000000000000' AND [PeriodId] <> '00000000-0000-0000-0000-000000000000' AND [ProposedByUserId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_JournalReversalProposals_MakerChecker", "[DecidedByUserId] IS NULL OR [DecidedByUserId] <> [ProposedByUserId]");
                    table.CheckConstraint("CK_JournalReversalProposals_Status", "([Status] = 1 AND [ReversalJournalId] IS NULL AND [DecidedByUserId] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionReason] IS NULL) OR ([Status] = 2 AND [ReversalJournalId] IS NOT NULL AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND [DecisionReason] IS NULL) OR ([Status] = 3 AND [ReversalJournalId] IS NULL AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL AND LEN([DecisionReason]) > 0)");
                    table.CheckConstraint("CK_JournalReversalProposals_Text", "LEN([Reason]) > 0");
                    table.CheckConstraint("CK_JournalReversalProposals_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_JournalReversalProposals_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalReversalProposals_Journals_CompanyId_ReversalJournalId",
                        columns: x => new { x.CompanyId, x.ReversalJournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalReversalProposals_Journals_CompanyId_SourceJournalId",
                        columns: x => new { x.CompanyId, x.SourceJournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalReversalProposals_CompanyId_ReversalJournalId",
                schema: "accounting",
                table: "JournalReversalProposals",
                columns: new[] { "CompanyId", "ReversalJournalId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalReversalProposals_CompanyId_SourceJournalId",
                schema: "accounting",
                table: "JournalReversalProposals",
                columns: new[] { "CompanyId", "SourceJournalId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalReversalProposals_CompanyId_Status_AccountingDate",
                schema: "accounting",
                table: "JournalReversalProposals",
                columns: new[] { "CompanyId", "Status", "AccountingDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JournalReversalProposals",
                schema: "accounting");
        }
    }
}
