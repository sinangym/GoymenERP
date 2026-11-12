using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialSalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfficialSalesInvoiceCommands",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialSalesInvoiceCommands", x => x.Id);
                    table.CheckConstraint("CK_OfficialSalesInvoiceCommands_Content", "LEN([CommandName]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([PayloadHash]) = 64 AND ISJSON([ResultJson]) = 1 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_OfficialSalesInvoiceCommands_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_OfficialSalesInvoiceCommands_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficialSalesInvoices",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfficialNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Ettn = table.Column<string>(type: "char(36)", unicode: false, fixedLength: true, maxLength: 36, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CustomerPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayableAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    PayableFunctionalAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    TaxBaseFunctionalAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    VatFunctionalAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    WithholdingFunctionalAmountText = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    PdfDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PdfDocumentVersionNo = table.Column<int>(type: "int", nullable: false),
                    PdfSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialSalesInvoices", x => x.Id);
                    table.UniqueConstraint("AK_OfficialSalesInvoices_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_OfficialSalesInvoices_Approval", "([Status] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [Version] = 1) OR ([Status] = 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [RecordedBy] COLLATE Latin1_General_100_BIN2)");
                    table.CheckConstraint("CK_OfficialSalesInvoices_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CustomerPartyId] <> '00000000-0000-0000-0000-000000000000' AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [PdfDocumentId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_OfficialSalesInvoices_Text", "LEN([OfficialNo]) > 0 AND DATALENGTH([Ettn]) = 36 AND [Ettn] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f-]%' AND LEN([PayableAmountText]) > 0 AND LEN([PayableFunctionalAmountText]) > 0 AND LEN([TaxBaseFunctionalAmountText]) > 0 AND LEN([VatFunctionalAmountText]) > 0 AND LEN([WithholdingFunctionalAmountText]) > 0 AND [PdfDocumentVersionNo] >= 1 AND DATALENGTH([PdfSha256]) = 64 AND [PdfSha256] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_OfficialSalesInvoices_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_OfficialSalesInvoices_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfficialSalesInvoiceCommands_CompanyId_CommandName_IdempotencyKeyHash",
                schema: "finance",
                table: "OfficialSalesInvoiceCommands",
                columns: new[] { "CompanyId", "CommandName", "IdempotencyKeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficialSalesInvoices_List",
                schema: "finance",
                table: "OfficialSalesInvoices",
                columns: new[] { "CompanyId", "Status", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_OfficialSalesInvoices_Ettn",
                schema: "finance",
                table: "OfficialSalesInvoices",
                columns: new[] { "CompanyId", "Ettn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OfficialSalesInvoices_OfficialNo",
                schema: "finance",
                table: "OfficialSalesInvoices",
                columns: new[] { "CompanyId", "OfficialNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Finance's module refusal (51051), forward fix: dropping a stored official sales invoice record (or its
            // command receipts) would lose an approved official number/ETTN, its PDF pin and who recorded/approved it
            // (FNS-D20: an approved record is never removed). An empty store rolls back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[OfficialSalesInvoices])
                    OR EXISTS (SELECT 1 FROM [finance].[OfficialSalesInvoiceCommands])
                BEGIN
                    THROW 51051, 'Finance official sales invoice records are stored; AddOfficialSalesInvoices is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "OfficialSalesInvoiceCommands",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "OfficialSalesInvoices",
                schema: "finance");
        }
    }
}
