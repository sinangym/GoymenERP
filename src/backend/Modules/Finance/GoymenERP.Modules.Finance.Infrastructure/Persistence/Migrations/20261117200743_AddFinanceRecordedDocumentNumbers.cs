using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceRecordedDocumentNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                schema: "finance",
                table: "ReceiptDrafts",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumberFormatVersion",
                schema: "finance",
                table: "ReceiptDrafts",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                schema: "finance",
                table: "ManualBankPayments",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumberFormatVersion",
                schema: "finance",
                table: "ManualBankPayments",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                schema: "finance",
                table: "ExpensePayments",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumberFormatVersion",
                schema: "finance",
                table: "ExpensePayments",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                schema: "finance",
                table: "CashTransfers",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumberFormatVersion",
                schema: "finance",
                table: "CashTransfers",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptDrafts_CompanyId_DocumentNumber",
                schema: "finance",
                table: "ReceiptDrafts",
                columns: new[] { "CompanyId", "DocumentNumber" },
                unique: true,
                filter: "[DocumentNumber] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReceiptDrafts_DocumentNumber",
                schema: "finance",
                table: "ReceiptDrafts",
                sql: "([DocumentNumber] IS NULL AND [DocumentNumberFormatVersion] IS NULL) OR ([Stage] = 1 AND [DocumentNumber] IS NOT NULL AND LEN([DocumentNumber]) BETWEEN 1 AND 64 AND [DocumentNumberFormatVersion] IS NOT NULL AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) IS NOT NULL AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) > 0 AND [DocumentNumberFormatVersion] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%' AND LEFT([DocumentNumberFormatVersion],1) BETWEEN '1' AND '9')");

            migrationBuilder.CreateIndex(
                name: "IX_ManualBankPayments_CompanyId_DocumentNumber",
                schema: "finance",
                table: "ManualBankPayments",
                columns: new[] { "CompanyId", "DocumentNumber" },
                unique: true,
                filter: "[DocumentNumber] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ManualBankPayments_DocumentNumber",
                schema: "finance",
                table: "ManualBankPayments",
                sql: "([DocumentNumber] IS NULL AND [DocumentNumberFormatVersion] IS NULL) OR ([Status] = 2 AND [DocumentNumber] IS NOT NULL AND LEN([DocumentNumber]) BETWEEN 1 AND 64 AND [DocumentNumberFormatVersion] IS NOT NULL AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) IS NOT NULL AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) > 0 AND [DocumentNumberFormatVersion] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%' AND LEFT([DocumentNumberFormatVersion],1) BETWEEN '1' AND '9')");

            migrationBuilder.CreateIndex(
                name: "IX_ExpensePayments_CompanyId_DocumentNumber",
                schema: "finance",
                table: "ExpensePayments",
                columns: new[] { "CompanyId", "DocumentNumber" },
                unique: true,
                filter: "[DocumentNumber] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ExpensePayments_DocumentNumber",
                schema: "finance",
                table: "ExpensePayments",
                sql: "([DocumentNumber] IS NULL AND [DocumentNumberFormatVersion] IS NULL) OR ([Status] = 2 AND [DocumentNumber] IS NOT NULL AND LEN([DocumentNumber]) BETWEEN 1 AND 64 AND [DocumentNumberFormatVersion] IS NOT NULL AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) IS NOT NULL AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) > 0 AND [DocumentNumberFormatVersion] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%' AND LEFT([DocumentNumberFormatVersion],1) BETWEEN '1' AND '9')");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_CompanyId_DocumentNumber",
                schema: "finance",
                table: "CashTransfers",
                columns: new[] { "CompanyId", "DocumentNumber" },
                unique: true,
                filter: "[DocumentNumber] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashTransfers_DocumentNumber",
                schema: "finance",
                table: "CashTransfers",
                sql: "([DocumentNumber] IS NULL AND [DocumentNumberFormatVersion] IS NULL) OR ([TransferConfirmed] = 1 AND [DocumentNumber] IS NOT NULL AND LEN([DocumentNumber]) BETWEEN 1 AND 64 AND [DocumentNumberFormatVersion] IS NOT NULL AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) IS NOT NULL AND TRY_CONVERT(bigint,[DocumentNumberFormatVersion]) > 0 AND [DocumentNumberFormatVersion] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%' AND LEFT([DocumentNumberFormatVersion],1) BETWEEN '1' AND '9')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[ReceiptDrafts] WHERE [DocumentNumber] IS NOT NULL OR [DocumentNumberFormatVersion] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [finance].[ManualBankPayments] WHERE [DocumentNumber] IS NOT NULL OR [DocumentNumberFormatVersion] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [finance].[ExpensePayments] WHERE [DocumentNumber] IS NOT NULL OR [DocumentNumberFormatVersion] IS NOT NULL)
                   OR EXISTS (SELECT 1 FROM [finance].[CashTransfers] WHERE [DocumentNumber] IS NOT NULL OR [DocumentNumberFormatVersion] IS NOT NULL)
                BEGIN
                    THROW 51911, 'Recorded finance document number proof exists; AddFinanceRecordedDocumentNumbers is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_ReceiptDrafts_CompanyId_DocumentNumber",
                schema: "finance",
                table: "ReceiptDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReceiptDrafts_DocumentNumber",
                schema: "finance",
                table: "ReceiptDrafts");

            migrationBuilder.DropIndex(
                name: "IX_ManualBankPayments_CompanyId_DocumentNumber",
                schema: "finance",
                table: "ManualBankPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ManualBankPayments_DocumentNumber",
                schema: "finance",
                table: "ManualBankPayments");

            migrationBuilder.DropIndex(
                name: "IX_ExpensePayments_CompanyId_DocumentNumber",
                schema: "finance",
                table: "ExpensePayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ExpensePayments_DocumentNumber",
                schema: "finance",
                table: "ExpensePayments");

            migrationBuilder.DropIndex(
                name: "IX_CashTransfers_CompanyId_DocumentNumber",
                schema: "finance",
                table: "CashTransfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashTransfers_DocumentNumber",
                schema: "finance",
                table: "CashTransfers");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                schema: "finance",
                table: "ReceiptDrafts");

            migrationBuilder.DropColumn(
                name: "DocumentNumberFormatVersion",
                schema: "finance",
                table: "ReceiptDrafts");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                schema: "finance",
                table: "ManualBankPayments");

            migrationBuilder.DropColumn(
                name: "DocumentNumberFormatVersion",
                schema: "finance",
                table: "ManualBankPayments");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                schema: "finance",
                table: "ExpensePayments");

            migrationBuilder.DropColumn(
                name: "DocumentNumberFormatVersion",
                schema: "finance",
                table: "ExpensePayments");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                schema: "finance",
                table: "CashTransfers");

            migrationBuilder.DropColumn(
                name: "DocumentNumberFormatVersion",
                schema: "finance",
                table: "CashTransfers");
        }
    }
}
