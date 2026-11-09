using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceDocumentAdmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceDocumentVoucherDrafts",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalizedVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExchangeRate = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MappingVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DocumentNet = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    DocumentVat = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    DocumentWithholding = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    DocumentPayable = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    FunctionalNet = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    FunctionalVat = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    FunctionalWithholding = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    FunctionalPayable = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDocumentVoucherDrafts", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_SourceDocumentVoucherDrafts_Amounts", "[DocumentNet] >= 0 AND [DocumentVat] >= 0 AND [DocumentWithholding] >= 0 AND [DocumentPayable] >= 0 AND [FunctionalNet] >= 0 AND [FunctionalVat] >= 0 AND [FunctionalWithholding] >= 0 AND [FunctionalPayable] >= 0 AND [FunctionalWithholding] <= [FunctionalVat] AND [FunctionalPayable] = [FunctionalNet] + [FunctionalVat] - [FunctionalWithholding]");
                    table.CheckConstraint("CK_SourceDocumentVoucherDrafts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Id] <> '00000000-0000-0000-0000-000000000000' AND [SourceDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [SourceActorId] <> '00000000-0000-0000-0000-000000000000' AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] >= 1 AND [MappingVersion] >= 1");
                    table.CheckConstraint("CK_SourceDocumentVoucherDrafts_Status", "[Status] = 1");
                    table.CheckConstraint("CK_SourceDocumentVoucherDrafts_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_SourceDocumentVoucherDrafts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceDocumentAdmissions",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalizedVersion = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SourceActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    VoucherDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    FirstAttemptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAttemptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDocumentAdmissions", x => new { x.CompanyId, x.SourceDocumentId });
                    table.CheckConstraint("CK_SourceDocumentAdmissions_Attempts", "[AttemptCount] >= 1 AND [Version] >= 1");
                    table.CheckConstraint("CK_SourceDocumentAdmissions_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_SourceDocumentAdmissions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND [SourceActorId] <> '00000000-0000-0000-0000-000000000000' AND [FinalizedVersion] >= 1 AND LEN([DocumentKind]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_SourceDocumentAdmissions_Status", "([Status] = 1 AND [VoucherDraftId] IS NULL AND LEN([ReasonCode]) > 0) OR ([Status] = 2 AND [VoucherDraftId] IS NOT NULL AND [ReasonCode] = 'admitted') OR ([Status] = 3 AND [VoucherDraftId] IS NULL AND LEN([ReasonCode]) > 0 AND [ReasonCode] <> 'admitted')");
                    table.ForeignKey(
                        name: "FK_SourceDocumentAdmissions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceDocumentAdmissions_SourceDocumentVoucherDrafts_CompanyId_VoucherDraftId",
                        columns: x => new { x.CompanyId, x.VoucherDraftId },
                        principalSchema: "accounting",
                        principalTable: "SourceDocumentVoucherDrafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceDocumentVoucherDraftLines",
                schema: "accounting",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    MappingKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Side = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    SourceLineNumber = table.Column<int>(type: "int", nullable: true),
                    VatRateCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDocumentVoucherDraftLines", x => new { x.CompanyId, x.DraftId, x.LineNumber });
                    table.CheckConstraint("CK_SourceDocumentVoucherDraftLines_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_SourceDocumentVoucherDraftLines_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [AccountId] <> '00000000-0000-0000-0000-000000000000' AND [LineNumber] >= 1 AND ([SourceLineNumber] IS NULL OR [SourceLineNumber] >= 1) AND LEN([MappingKey]) > 0");
                    table.CheckConstraint("CK_SourceDocumentVoucherDraftLines_Side", "[Side] IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_SourceDocumentVoucherDraftLines_SourceDocumentVoucherDrafts_CompanyId_DraftId",
                        columns: x => new { x.CompanyId, x.DraftId },
                        principalSchema: "accounting",
                        principalTable: "SourceDocumentVoucherDrafts",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_SourceDocumentAdmissions_Document",
                schema: "accounting",
                table: "SourceDocumentAdmissions",
                columns: new[] { "CompanyId", "DocumentKind", "InvoiceId", "FinalizedVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SourceDocumentAdmissions_Draft",
                schema: "accounting",
                table: "SourceDocumentAdmissions",
                columns: new[] { "CompanyId", "VoucherDraftId" },
                unique: true,
                filter: "[VoucherDraftId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_SourceDocumentAdmissions_Key",
                schema: "accounting",
                table: "SourceDocumentAdmissions",
                columns: new[] { "CompanyId", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SourceDocumentVoucherDrafts_Source",
                schema: "accounting",
                table: "SourceDocumentVoucherDrafts",
                columns: new[] { "CompanyId", "SourceDocumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a stored admission (waiting, admitted or refused) is the attempt/refusal record of a source
            // document and a draft voucher is an accounting fact; a drop would lose them, so while either exists the
            // rollback refuses (51240, used by no other migration).
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [accounting].[SourceDocumentAdmissions])
                   OR EXISTS (SELECT 1 FROM [accounting].[SourceDocumentVoucherDrafts])
                BEGIN
                    ;THROW 51240, N'Accounting source document admissions exist; AddSourceDocumentAdmissions is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "SourceDocumentAdmissions",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "SourceDocumentVoucherDraftLines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "SourceDocumentVoucherDrafts",
                schema: "accounting");
        }
    }
}
