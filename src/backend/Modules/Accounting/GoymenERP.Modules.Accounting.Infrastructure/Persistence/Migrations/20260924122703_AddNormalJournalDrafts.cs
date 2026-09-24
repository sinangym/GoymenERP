using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalJournalDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NormalJournalDrafts",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Consumer = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    MessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingPurpose = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PostingRuleVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AccountingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AccountPlanSourceVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AccountPlanSourceSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    AccountPlanApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountPlanApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostingRuleApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingRuleApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalJournalDrafts", x => x.Id);
                    table.UniqueConstraint("AK_NormalJournalDrafts_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_NormalJournalDrafts_Hashes", "LEN([ContentHash]) = 64 AND LEN([AccountPlanSourceSha256]) = 64");
                    table.CheckConstraint("CK_NormalJournalDrafts_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [PreparedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [AccountPlanApprovedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [PostingRuleApprovedByUserId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_NormalJournalDrafts_Status", "([Status] = 1 AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL AND [PeriodId] IS NULL AND [PostedJournalId] IS NULL) OR ([Status] = 2 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ApprovedAtUtc] IS NOT NULL AND [PeriodId] IS NOT NULL AND [PeriodId] <> '00000000-0000-0000-0000-000000000000' AND [PostedJournalId] = [Id])");
                    table.CheckConstraint("CK_NormalJournalDrafts_Text", "LEN([Consumer]) > 0 AND LEN([MessageId]) > 0 AND LEN([PostingPurpose]) > 0 AND LEN([PostingRuleVersion]) > 0 AND LEN([AccountPlanSourceVersion]) > 0");
                    table.CheckConstraint("CK_NormalJournalDrafts_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_NormalJournalDrafts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NormalJournalDrafts_Journals_CompanyId_PostedJournalId",
                        columns: x => new { x.CompanyId, x.PostedJournalId },
                        principalSchema: "accounting",
                        principalTable: "Journals",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NormalJournalDraftLines",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    MappingKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Side = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalJournalDraftLines", x => new { x.CompanyId, x.DraftId, x.LineNumber });
                    table.CheckConstraint("CK_NormalJournalDraftLines_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [AccountId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_NormalJournalDraftLines_Line", "[LineNumber] >= 1 AND LEN([MappingKey]) > 0");
                    table.CheckConstraint("CK_NormalJournalDraftLines_Value", "[Side] IN (1, 2) AND [Amount] > 0");
                    table.ForeignKey(
                        name: "FK_NormalJournalDraftLines_NormalJournalDrafts_CompanyId_DraftId",
                        columns: x => new { x.CompanyId, x.DraftId },
                        principalSchema: "accounting",
                        principalTable: "NormalJournalDrafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NormalJournalDrafts_CompanyId_PostedJournalId",
                schema: "accounting",
                table: "NormalJournalDrafts",
                columns: new[] { "CompanyId", "PostedJournalId" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalJournalDrafts_CompanyId_Status_AccountingDate",
                schema: "accounting",
                table: "NormalJournalDrafts",
                columns: new[] { "CompanyId", "Status", "AccountingDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [accounting].[NormalJournalDrafts])
                    OR EXISTS (SELECT 1 FROM [accounting].[NormalJournalDraftLines])
                    THROW 51048, 'Accounting normal-journal drafts require a forward fix.', 1;
                """);
            migrationBuilder.DropTable(
                name: "NormalJournalDraftLines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "NormalJournalDrafts",
                schema: "accounting");
        }
    }
}
