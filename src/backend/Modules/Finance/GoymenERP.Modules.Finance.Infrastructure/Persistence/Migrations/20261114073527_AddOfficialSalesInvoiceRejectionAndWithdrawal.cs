using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialSalesInvoiceRejectionAndWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OfficialSalesInvoices_Ettn",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropIndex(
                name: "UX_OfficialSalesInvoices_OfficialNo",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropIndex(
                name: "UX_OfficialSalesInvoices_PdfSha256",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OfficialSalesInvoices_Approval",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAtUtc",
                schema: "finance",
                table: "OfficialSalesInvoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedBy",
                schema: "finance",
                table: "OfficialSalesInvoices",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "finance",
                table: "OfficialSalesInvoices",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WithdrawnAtUtc",
                schema: "finance",
                table: "OfficialSalesInvoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawnBy",
                schema: "finance",
                table: "OfficialSalesInvoices",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateIndex(
                name: "UX_OfficialSalesInvoices_Ettn",
                schema: "finance",
                table: "OfficialSalesInvoices",
                columns: new[] { "CompanyId", "Ettn" },
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_OfficialSalesInvoices_OfficialNo",
                schema: "finance",
                table: "OfficialSalesInvoices",
                columns: new[] { "CompanyId", "OfficialNo" },
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "UX_OfficialSalesInvoices_PdfSha256",
                schema: "finance",
                table: "OfficialSalesInvoices",
                columns: new[] { "CompanyId", "PdfSha256" },
                unique: true,
                filter: "[Status] IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OfficialSalesInvoices_Approval",
                schema: "finance",
                table: "OfficialSalesInvoices",
                sql: "([Status] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [Version] = 1 AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [WithdrawnBy] IS NULL AND [WithdrawnAtUtc] IS NULL) OR ([Status] = 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [RecordedBy] COLLATE Latin1_General_100_BIN2 AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL AND [WithdrawnBy] IS NULL AND [WithdrawnAtUtc] IS NULL) OR ([Status] = 3 AND [RejectedBy] IS NOT NULL AND [RejectedAtUtc] IS NOT NULL AND LEN([RejectedBy]) > 0 AND [RejectedBy] COLLATE Latin1_General_100_BIN2 <> [RecordedBy] COLLATE Latin1_General_100_BIN2 AND [RejectionReason] IS NOT NULL AND LEN([RejectionReason]) > 0 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [WithdrawnBy] IS NULL AND [WithdrawnAtUtc] IS NULL) OR ([Status] = 4 AND [WithdrawnBy] IS NOT NULL AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnBy] COLLATE Latin1_General_100_BIN2 = [RecordedBy] COLLATE Latin1_General_100_BIN2 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [RejectedBy] IS NULL AND [RejectedAtUtc] IS NULL AND [RejectionReason] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Finance's module refusal (51273), forward fix: a rejected or withdrawn record (FNS-D39) would lose its
            // decision trail (who rejected/withdrew it, when, and the rejection reason) and could not satisfy the old
            // pending/approved check; restoring the unfiltered unique indexes could also collide with a live record
            // that reuses its official number, ETTN or PDF. Pending and approved records roll back freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[OfficialSalesInvoices] WHERE [Status] IN (3, 4))
                BEGIN
                    THROW 51273, 'Finance official sales invoice records are rejected or withdrawn; AddOfficialSalesInvoiceRejectionAndWithdrawal is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "UX_OfficialSalesInvoices_Ettn",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropIndex(
                name: "UX_OfficialSalesInvoices_OfficialNo",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropIndex(
                name: "UX_OfficialSalesInvoices_PdfSha256",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OfficialSalesInvoices_Approval",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropColumn(
                name: "WithdrawnAtUtc",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropColumn(
                name: "WithdrawnBy",
                schema: "finance",
                table: "OfficialSalesInvoices");

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

            migrationBuilder.CreateIndex(
                name: "UX_OfficialSalesInvoices_PdfSha256",
                schema: "finance",
                table: "OfficialSalesInvoices",
                columns: new[] { "CompanyId", "PdfSha256" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OfficialSalesInvoices_Approval",
                schema: "finance",
                table: "OfficialSalesInvoices",
                sql: "([Status] = 1 AND [ApprovedBy] IS NULL AND [ApprovedAtUtc] IS NULL AND [Version] = 1) OR ([Status] = 2 AND [ApprovedBy] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND LEN([ApprovedBy]) > 0 AND [ApprovedBy] COLLATE Latin1_General_100_BIN2 <> [RecordedBy] COLLATE Latin1_General_100_BIN2)");
        }
    }
}
