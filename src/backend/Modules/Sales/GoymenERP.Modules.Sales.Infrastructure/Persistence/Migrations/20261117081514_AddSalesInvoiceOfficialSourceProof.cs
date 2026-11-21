using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceOfficialSourceProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "OfficialInvoiceDate",
                schema: "sales",
                table: "InvoiceMatches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialSourceProofJson",
                schema: "sales",
                table: "InvoiceMatches",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceMatches_SourceProof",
                schema: "sales",
                table: "InvoiceMatches",
                sql: "([OfficialInvoiceDate] IS NULL AND [OfficialSourceProofJson] IS NULL) OR (CASE WHEN [OfficialInvoiceDate] > '0001-01-01' AND [OfficialSourceProofJson] IS NOT NULL AND ISJSON([OfficialSourceProofJson]) = 1 AND TRY_CONVERT(uniqueidentifier, JSON_VALUE([OfficialSourceProofJson], '$.companyId')) = [CompanyId] AND TRY_CONVERT(uniqueidentifier, JSON_VALUE([OfficialSourceProofJson], '$.invoiceId')) <> '00000000-0000-0000-0000-000000000000' AND LEN(JSON_VALUE([OfficialSourceProofJson], '$.version')) BETWEEN 1 AND 128 AND TRY_CONVERT(datetimeoffset, JSON_VALUE([OfficialSourceProofJson], '$.approvedAtUtc')) > '0001-01-01' AND ((JSON_VALUE([OfficialSourceProofJson], '$.sourceKind') = 'manual' AND TRY_CONVERT(uniqueidentifier, JSON_VALUE([OfficialSourceProofJson], '$.pdfDocumentId')) <> '00000000-0000-0000-0000-000000000000' AND TRY_CONVERT(int, JSON_VALUE([OfficialSourceProofJson], '$.pdfDocumentVersionNo')) > 0 AND LEN(JSON_VALUE([OfficialSourceProofJson], '$.pdfSha256')) = 64 AND JSON_VALUE([OfficialSourceProofJson], '$.pdfSha256') COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-fA-F]%' AND JSON_VALUE([OfficialSourceProofJson], '$.sourceBatchId') IS NULL AND JSON_VALUE([OfficialSourceProofJson], '$.sourceChecksum') IS NULL AND JSON_VALUE([OfficialSourceProofJson], '$.hasPendingManual') = 'false') OR (JSON_VALUE([OfficialSourceProofJson], '$.sourceKind') = 'approved-import' AND TRY_CONVERT(uniqueidentifier, JSON_VALUE([OfficialSourceProofJson], '$.sourceBatchId')) <> '00000000-0000-0000-0000-000000000000' AND LEN(JSON_VALUE([OfficialSourceProofJson], '$.sourceChecksum')) = 64 AND JSON_VALUE([OfficialSourceProofJson], '$.sourceChecksum') COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-fA-F]%' AND JSON_VALUE([OfficialSourceProofJson], '$.pdfDocumentId') IS NULL AND JSON_VALUE([OfficialSourceProofJson], '$.pdfDocumentVersionNo') IS NULL AND JSON_VALUE([OfficialSourceProofJson], '$.pdfSha256') IS NULL AND JSON_VALUE([OfficialSourceProofJson], '$.hasPendingManual') IN ('true', 'false'))) THEN 1 ELSE 0 END = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[InvoiceMatches]
                    WHERE [OfficialInvoiceDate] IS NOT NULL OR [OfficialSourceProofJson] IS NOT NULL)
                BEGIN
                    THROW 51982, 'Official invoice source evidence exists; AddSalesInvoiceOfficialSourceProof is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceMatches_SourceProof",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.DropColumn(
                name: "OfficialInvoiceDate",
                schema: "sales",
                table: "InvoiceMatches");

            migrationBuilder.DropColumn(
                name: "OfficialSourceProofJson",
                schema: "sales",
                table: "InvoiceMatches");
        }
    }
}
