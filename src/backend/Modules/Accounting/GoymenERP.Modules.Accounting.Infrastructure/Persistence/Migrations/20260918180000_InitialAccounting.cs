using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations;

/// <summary>
/// Accounting's first schema (ACC-3): periods and their per-company calendar, journals with their
/// lines, the delivery inbox, and the exception and pending-valuation queues. Every table is a
/// tenant table; companies are Organization's, referenced by key only, so this runs after
/// Organization's migrations.
/// </summary>
[DbContext(typeof(AccountingDbContext))]
[Migration("20260918180000_InitialAccounting")]
public sealed class InitialAccounting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "accounting");

        migrationBuilder.CreateTable(
            name: "PeriodCalendars",
            schema: "accounting",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PeriodCalendars", x => x.CompanyId);
                table.CheckConstraint("CK_PeriodCalendars_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_PeriodCalendars_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_PeriodCalendars_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Periods",
            schema: "accounting",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Periods", x => x.Id);
                table.UniqueConstraint("AK_Periods_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Periods_Dates", "[EndsOn] >= [StartsOn]");
                table.CheckConstraint("CK_Periods_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Periods_Status",
                    "([Status] = 1 AND [ClosedByUserId] IS NULL AND [ClosedAtUtc] IS NULL) OR "
                    + "([Status] = 2 AND [ClosedByUserId] IS NOT NULL AND [ClosedByUserId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [ClosedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_Periods_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_Periods_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PostingExceptions",
            schema: "accounting",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PostingPurpose = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                Status = table.Column<int>(type: "int", nullable: false),
                PostingRuleVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                MissingMappingKeysJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                FirstMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PostingExceptions", x => new { x.CompanyId, x.SourceId, x.PostingPurpose });
                table.CheckConstraint("CK_PostingExceptions_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND LEN([PostingPurpose]) > 0 AND LEN([PostingRuleVersion]) > 0 AND LEN([FirstMessageId]) > 0");
                table.CheckConstraint("CK_PostingExceptions_Keys", "ISJSON([MissingMappingKeysJson]) = 1");
                table.CheckConstraint("CK_PostingExceptions_Status", "[Status] IN (1, 2)");
                table.CheckConstraint("CK_PostingExceptions_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_PostingExceptions_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PendingValuations",
            schema: "accounting",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PostingPurpose = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                Status = table.Column<int>(type: "int", nullable: false),
                PostingRuleVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                LinesAwaitingAmount = table.Column<int>(type: "int", nullable: false),
                FirstMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PendingValuations", x => new { x.CompanyId, x.SourceId, x.PostingPurpose });
                table.CheckConstraint("CK_PendingValuations_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND LEN([PostingPurpose]) > 0 AND LEN([PostingRuleVersion]) > 0 AND LEN([FirstMessageId]) > 0");
                table.CheckConstraint("CK_PendingValuations_Lines", "[LinesAwaitingAmount] >= 1");
                table.CheckConstraint("CK_PendingValuations_Status", "[Status] IN (1, 2)");
                table.CheckConstraint("CK_PendingValuations_Version", "[Version] >= 1");
                table.ForeignKey(
                    name: "FK_PendingValuations_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Journals",
            schema: "accounting",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<int>(type: "int", nullable: false),
                SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PostingPurpose = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                PostingRuleVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                AccountingDate = table.Column<DateOnly>(type: "date", nullable: false),
                ValueDate = table.Column<DateOnly>(type: "date", nullable: true),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LinkedJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CorrectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StoredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                StoredBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Journals", x => x.Id);
                table.UniqueConstraint("AK_Journals_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Journals_Identity",
                    "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [SourceId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' "
                    + "AND [PeriodId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_Journals_Kind",
                    "([Kind] = 1 AND [LinkedJournalId] IS NULL AND [CorrectionId] IS NULL) OR "
                    + "([Kind] = 2 AND [LinkedJournalId] IS NOT NULL AND [CorrectionId] IS NULL) OR "
                    + "([Kind] = 3 AND [LinkedJournalId] IS NOT NULL AND [CorrectionId] IS NOT NULL "
                    + "AND [CorrectionId] <> '00000000-0000-0000-0000-000000000000')");
                table.CheckConstraint("CK_Journals_Text",
                    "LEN([PostingPurpose]) > 0 AND LEN([PostingRuleVersion]) > 0 AND LEN([StoredBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_Journals_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Journals_Journals_CompanyId_LinkedJournalId",
                    columns: x => new { x.CompanyId, x.LinkedJournalId },
                    principalSchema: "accounting",
                    principalTable: "Journals",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Journals_Periods_CompanyId_PeriodId",
                    columns: x => new { x.CompanyId, x.PeriodId },
                    principalSchema: "accounting",
                    principalTable: "Periods",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "JournalLines",
            schema: "accounting",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LineNumber = table.Column<int>(type: "int", nullable: false),
                AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Debit = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                Credit = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JournalLines", x => new { x.CompanyId, x.JournalId, x.LineNumber });
                table.CheckConstraint("CK_JournalLines_Account", "[AccountId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_JournalLines_LineNumber", "[LineNumber] >= 1");
                table.CheckConstraint("CK_JournalLines_OneSide", "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)");
                table.ForeignKey(
                    name: "FK_JournalLines_Journals_CompanyId_JournalId",
                    columns: x => new { x.CompanyId, x.JournalId },
                    principalSchema: "accounting",
                    principalTable: "Journals",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Deliveries",
            schema: "accounting",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Consumer = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                MessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false,
                    collation: "Latin1_General_100_BIN2"),
                OutcomeKind = table.Column<int>(type: "int", nullable: false),
                JournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                QueueEntryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Deliveries", x => new { x.CompanyId, x.Consumer, x.MessageId });
                table.CheckConstraint("CK_Deliveries_Identity",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([Consumer]) > 0 AND LEN([MessageId]) > 0 "
                    + "AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_Deliveries_Outcome",
                    "([OutcomeKind] IN (1, 3, 6) AND [JournalId] IS NOT NULL AND [QueueEntryJson] IS NULL) OR "
                    + "([OutcomeKind] IN (4, 5) AND [JournalId] IS NULL AND [QueueEntryJson] IS NOT NULL AND ISJSON([QueueEntryJson]) = 1)");
                table.ForeignKey(
                    name: "FK_Deliveries_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Deliveries_Journals_CompanyId_JournalId",
                    columns: x => new { x.CompanyId, x.JournalId },
                    principalSchema: "accounting",
                    principalTable: "Journals",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Periods_CompanyId_StartsOn_EndsOn",
            schema: "accounting",
            table: "Periods",
            columns: new[] { "CompanyId", "StartsOn", "EndsOn" });

        migrationBuilder.CreateIndex(
            name: "IX_Journals_CompanyId_PeriodId",
            schema: "accounting",
            table: "Journals",
            columns: new[] { "CompanyId", "PeriodId" });

        migrationBuilder.CreateIndex(
            name: "UX_Journals_Original",
            schema: "accounting",
            table: "Journals",
            columns: new[] { "CompanyId", "SourceId", "PostingPurpose" },
            unique: true,
            filter: "[Kind] = 1");

        migrationBuilder.CreateIndex(
            name: "UX_Journals_Reversal",
            schema: "accounting",
            table: "Journals",
            columns: new[] { "CompanyId", "LinkedJournalId" },
            unique: true,
            filter: "[Kind] = 2");

        migrationBuilder.CreateIndex(
            name: "UX_Journals_Correction",
            schema: "accounting",
            table: "Journals",
            columns: new[] { "CompanyId", "CorrectionId" },
            unique: true,
            filter: "[CorrectionId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Deliveries_CompanyId_JournalId",
            schema: "accounting",
            table: "Deliveries",
            columns: new[] { "CompanyId", "JournalId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Deliveries", schema: "accounting");
        migrationBuilder.DropTable(name: "JournalLines", schema: "accounting");
        migrationBuilder.DropTable(name: "Journals", schema: "accounting");
        migrationBuilder.DropTable(name: "PendingValuations", schema: "accounting");
        migrationBuilder.DropTable(name: "PostingExceptions", schema: "accounting");
        migrationBuilder.DropTable(name: "Periods", schema: "accounting");
        migrationBuilder.DropTable(name: "PeriodCalendars", schema: "accounting");
    }
}
