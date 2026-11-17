using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceVoucherLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SourceDocumentVoucherDrafts_Status",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JournalId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialInvoiceNo",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SourceInvoiceCancellations",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancellationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalizedVersion = table.Column<long>(type: "bigint", nullable: false),
                    CancelledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceInvoiceCancellations", x => new { x.CompanyId, x.CancellationId });
                    table.CheckConstraint("CK_SourceInvoiceCancellations_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CancellationId] <> '00000000-0000-0000-0000-000000000000' AND [SourceDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [CancelledBy] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] > 0");
                    table.CheckConstraint("CK_SourceInvoiceCancellations_Kind", "[DocumentKind] IN ('sales-invoice','purchase-invoice')");
                    table.CheckConstraint("CK_SourceInvoiceCancellations_Reason", "LEN([Reason]) > 0");
                    table.ForeignKey(
                        name: "FK_SourceInvoiceCancellations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocumentVoucherDrafts_CompanyId_JournalId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                columns: new[] { "CompanyId", "JournalId" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocumentVoucherDrafts_CompanyId_PeriodId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                columns: new[] { "CompanyId", "PeriodId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SourceDocumentVoucherDrafts_Status",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                sql: "([Status] IN (1,3) AND [JournalId] IS NULL AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL) OR ([Status] = 2 AND [JournalId] IS NOT NULL AND [JournalId] = [Id] AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [ApprovedBy] <> [SourceActorId] AND [ApprovedBy] <> '00000000-0000-0000-0000-000000000000' AND [ApprovedAtUtc] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SourceInvoiceCancellations_CompanyId_DocumentKind_InvoiceId_FinalizedVersion",
                schema: "accounting",
                table: "SourceInvoiceCancellations",
                columns: new[] { "CompanyId", "DocumentKind", "InvoiceId", "FinalizedVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceInvoiceCancellations_CompanyId_SourceDocumentId",
                schema: "accounting",
                table: "SourceInvoiceCancellations",
                columns: new[] { "CompanyId", "SourceDocumentId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SourceDocumentVoucherDrafts_Journals_CompanyId_JournalId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                columns: new[] { "CompanyId", "JournalId" },
                principalSchema: "accounting",
                principalTable: "Journals",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SourceDocumentVoucherDrafts_Periods_CompanyId_PeriodId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                columns: new[] { "CompanyId", "PeriodId" },
                principalSchema: "accounting",
                principalTable: "Periods",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[SourceInvoiceCancellations])
                    OR EXISTS (SELECT 1 FROM [accounting].[SourceDocumentVoucherDrafts]
                        WHERE [Status] <> 1 OR [OfficialInvoiceNo] IS NOT NULL OR [JournalId] IS NOT NULL
                            OR [ApprovedBy] IS NOT NULL OR [ApprovedAtUtc] IS NOT NULL)
                BEGIN
                    THROW 51938, 'Source voucher approval, cancellation or official document facts exist; AddSourceVoucherLifecycle is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_SourceDocumentVoucherDrafts_Journals_CompanyId_JournalId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_SourceDocumentVoucherDrafts_Periods_CompanyId_PeriodId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.DropTable(
                name: "SourceInvoiceCancellations",
                schema: "accounting");

            migrationBuilder.DropIndex(
                name: "IX_SourceDocumentVoucherDrafts_CompanyId_JournalId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.DropIndex(
                name: "IX_SourceDocumentVoucherDrafts_CompanyId_PeriodId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SourceDocumentVoucherDrafts_Status",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.DropColumn(
                name: "JournalId",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.DropColumn(
                name: "OfficialInvoiceNo",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SourceDocumentVoucherDrafts_Status",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                sql: "[Status] = 1");
        }
    }
}
