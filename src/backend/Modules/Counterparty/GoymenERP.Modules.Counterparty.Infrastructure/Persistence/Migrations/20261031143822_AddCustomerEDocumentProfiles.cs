using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// SAT-D119: the group-level, VKN-keyed customer e-belge profile table (status + check time, checker and checking
    /// company + scenario override), shared across all companies; no CompanyId tenant key (the same group-wide shape as
    /// Parties). One row per taxpayer (unique VKN/TCKN); the checking company is a foreign key to the company, used only
    /// to decide who may see the checker. Four check constraints (VKN digits, version, status/check consistency,
    /// override shape). Nothing is seeded. Rollback is refused with 51122 while any profile exists, so no recorded status
    /// or override is dropped silently; with an empty table it drops the table. Forward-fix plan otherwise: keep the
    /// additive table (no other module reads it; consumers use the port) and correct with a new migration.
    /// </summary>
    public partial class AddCustomerEDocumentProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerEDocumentProfiles",
                schema: "counterparty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxIdentityNumber = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    EDocumentStatus = table.Column<int>(type: "int", nullable: false),
                    EDocumentStatusCheckedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EDocumentStatusCheckedByUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EDocumentStatusCheckedInCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EInvoiceScenarioOverrideCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerEDocumentProfiles", x => x.Id);
                    table.CheckConstraint("CK_CustomerEDocumentProfiles_ScenarioOverrideCode", "[EInvoiceScenarioOverrideCode] IS NULL OR (LEN([EInvoiceScenarioOverrideCode]) > 0 AND DATALENGTH([EInvoiceScenarioOverrideCode]) = 2 * LEN([EInvoiceScenarioOverrideCode]) AND [EInvoiceScenarioOverrideCode] COLLATE Latin1_General_100_BIN2 LIKE '[A-Z]%' AND [EInvoiceScenarioOverrideCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_]%')");
                    table.CheckConstraint("CK_CustomerEDocumentProfiles_Status", "([EDocumentStatus] = 0 AND [EDocumentStatusCheckedAtUtc] IS NULL AND [EDocumentStatusCheckedByUserId] IS NULL AND [EDocumentStatusCheckedInCompanyId] IS NULL) OR ([EDocumentStatus] IN (1, 2) AND [EDocumentStatusCheckedAtUtc] IS NOT NULL AND [EDocumentStatusCheckedByUserId] IS NOT NULL AND LEN([EDocumentStatusCheckedByUserId]) > 0 AND [EDocumentStatusCheckedInCompanyId] IS NOT NULL)");
                    table.CheckConstraint("CK_CustomerEDocumentProfiles_TaxIdentityNumber", "LEN([TaxIdentityNumber]) IN (10, 11) AND DATALENGTH([TaxIdentityNumber]) = LEN([TaxIdentityNumber]) AND [TaxIdentityNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'");
                    table.CheckConstraint("CK_CustomerEDocumentProfiles_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_CustomerEDocumentProfiles_Companies_CheckedInCompanyId",
                        column: x => x.EDocumentStatusCheckedInCompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerEDocumentProfiles_EDocumentStatusCheckedInCompanyId",
                schema: "counterparty",
                table: "CustomerEDocumentProfiles",
                column: "EDocumentStatusCheckedInCompanyId");

            migrationBuilder.CreateIndex(
                name: "UX_CustomerEDocumentProfiles_TaxIdentityNumber",
                schema: "counterparty",
                table: "CustomerEDocumentProfiles",
                column: "TaxIdentityNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [counterparty].[CustomerEDocumentProfiles])
                BEGIN
                    THROW 51122, 'Rollback blocked: counterparty customer e-document profiles exist. Keep the schema and use a compatible forward fix.', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CustomerEDocumentProfiles",
                schema: "counterparty");
        }
    }
}
