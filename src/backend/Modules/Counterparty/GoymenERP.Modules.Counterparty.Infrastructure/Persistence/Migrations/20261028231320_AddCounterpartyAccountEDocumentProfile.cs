using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// SAT-D102: the e-belge profile on the company account — the e-Fatura / e-Arşiv status (0 = Unknown for every
    /// existing account: nothing is inferred), the time and checker of the last check (both null exactly when Unknown,
    /// check constraint), and the optional customer e-Fatura scenario override (lexical shape only; WHICH codes exist is
    /// the Settings list's business). No value is seeded. Rollback is refused with 51121 while any account carries a
    /// recorded status or an override, so no checked status is ever dropped silently; with only Unknown statuses and no
    /// override it drops the constraints and the four columns. Forward-fix plan otherwise: keep the columns (they are
    /// additive and ignored by older code) and correct with a new migration.
    /// </summary>
    public partial class AddCounterpartyAccountEDocumentProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EDocumentStatus",
                schema: "counterparty",
                table: "CompanyPartyAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EDocumentStatusCheckedAtUtc",
                schema: "counterparty",
                table: "CompanyPartyAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EDocumentStatusCheckedByUserId",
                schema: "counterparty",
                table: "CompanyPartyAccounts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EInvoiceScenarioOverrideCode",
                schema: "counterparty",
                table: "CompanyPartyAccounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyPartyAccounts_EDocumentStatus",
                schema: "counterparty",
                table: "CompanyPartyAccounts",
                sql: "([EDocumentStatus] = 0 AND [EDocumentStatusCheckedAtUtc] IS NULL AND [EDocumentStatusCheckedByUserId] IS NULL) OR ([EDocumentStatus] IN (1, 2) AND [EDocumentStatusCheckedAtUtc] IS NOT NULL AND [EDocumentStatusCheckedByUserId] IS NOT NULL AND LEN([EDocumentStatusCheckedByUserId]) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanyPartyAccounts_EInvoiceScenarioOverrideCode",
                schema: "counterparty",
                table: "CompanyPartyAccounts",
                sql: "[EInvoiceScenarioOverrideCode] IS NULL OR (LEN([EInvoiceScenarioOverrideCode]) > 0 AND DATALENGTH([EInvoiceScenarioOverrideCode]) = 2 * LEN([EInvoiceScenarioOverrideCode]) AND [EInvoiceScenarioOverrideCode] COLLATE Latin1_General_100_BIN2 LIKE '[A-Z]%' AND [EInvoiceScenarioOverrideCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_]%')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [counterparty].[CompanyPartyAccounts]
                           WHERE [EDocumentStatus] <> 0 OR [EInvoiceScenarioOverrideCode] IS NOT NULL)
                BEGIN
                    THROW 51121, 'Rollback blocked: counterparty account e-document statuses or e-invoice scenario overrides exist. Keep the schema and use a compatible forward fix.', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyPartyAccounts_EDocumentStatus",
                schema: "counterparty",
                table: "CompanyPartyAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanyPartyAccounts_EInvoiceScenarioOverrideCode",
                schema: "counterparty",
                table: "CompanyPartyAccounts");

            migrationBuilder.DropColumn(
                name: "EDocumentStatus",
                schema: "counterparty",
                table: "CompanyPartyAccounts");

            migrationBuilder.DropColumn(
                name: "EDocumentStatusCheckedAtUtc",
                schema: "counterparty",
                table: "CompanyPartyAccounts");

            migrationBuilder.DropColumn(
                name: "EDocumentStatusCheckedByUserId",
                schema: "counterparty",
                table: "CompanyPartyAccounts");

            migrationBuilder.DropColumn(
                name: "EInvoiceScenarioOverrideCode",
                schema: "counterparty",
                table: "CompanyPartyAccounts");
        }
    }
}
