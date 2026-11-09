using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// S5 API-35 (Fatura çekirdeği 3, K3): the append-only official match of an approved invoice (SAT-D89, SAT-D106, SAT-D229) in
    /// <c>sales.InvoiceMatches</c> (one row per invoice; unique official number and ETTN per company) with the SQL trigger
    /// <c>TR_InvoiceMatches_Guard</c> refusing every UPDATE and DELETE (THROW 51226) and the invoice attempt command check widened
    /// with <c>sales.invoice.match.v1</c>. The approved invoice row is not touched (TR_Invoices_Guard freezes it). No existing row is rewritten.
    /// Rollback: refused once a match or a match attempt exists (THROW 51225 — forward fix); an installation without one rolls back cleanly.
    /// </summary>
    public partial class AddSalesInvoiceOfficialMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts");

            migrationBuilder.CreateTable(
                name: "InvoiceMatches",
                schema: "sales",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OfficialInvoiceNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Ettn = table.Column<string>(type: "varchar(36)", unicode: false, maxLength: 36, nullable: false, collation: "Latin1_General_100_BIN2"),
                    InvoiceAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    OfficialAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    Tolerance = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    MatchedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MatchedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DifferenceApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DifferenceReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceMatches", x => new { x.CompanyId, x.InvoiceId });
                    table.CheckConstraint("CK_InvoiceMatches_Amounts", "[Difference] >= 0 AND [Tolerance] >= 0 AND [InvoiceAmount] >= 0 AND [OfficialAmount] >= 0 AND ([Difference] = ABS([InvoiceAmount] - [OfficialAmount])) AND (([Difference] <= [Tolerance] AND [DifferenceApprovedBy] IS NULL AND [DifferenceReason] IS NULL) OR ([Difference] > [Tolerance] AND [DifferenceApprovedBy] IS NOT NULL AND [DifferenceReason] IS NOT NULL))");
                    table.CheckConstraint("CK_InvoiceMatches_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [InvoiceId] <> '00000000-0000-0000-0000-000000000000' AND LEN([OfficialInvoiceNo]) > 0 AND LEN([Ettn]) = 36 AND LEN([MatchedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.ForeignKey(
                        name: "FK_InvoiceMatches_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceMatches_Invoices_CompanyId_InvoiceId",
                        columns: x => new { x.CompanyId, x.InvoiceId },
                        principalSchema: "sales",
                        principalTable: "Invoices",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts",
                sql: "[CommandName] IN (N'sales.invoice.create.v1', N'sales.invoice.submit.v1', N'sales.invoice.approve.v1', N'sales.invoice.reject.v1', N'sales.invoice.cancel.v1', N'sales.invoice.match.v1')");

            migrationBuilder.CreateIndex(
                name: "UX_InvoiceMatches_Ettn",
                schema: "sales",
                table: "InvoiceMatches",
                columns: new[] { "CompanyId", "Ettn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InvoiceMatches_OfficialNo",
                schema: "sales",
                table: "InvoiceMatches",
                columns: new[] { "CompanyId", "OfficialInvoiceNo" },
                unique: true);

            // API-35 / SAT-D89: an official match is evidence; it is never changed or deleted (51226; 51225 is the Down guard).
            migrationBuilder.Sql("""
                CREATE TRIGGER [sales].[TR_InvoiceMatches_Guard]
                ON [sales].[InvoiceMatches]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51226, 'Sales invoice official matches are immutable.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward fix: a recorded match is official evidence that the previous model cannot hold. The guard refuses (51225) before
            // anything is dropped; an installation without a match or a match attempt rolls back (dropping the table drops its trigger).
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[InvoiceMatches])
                   OR EXISTS (SELECT 1 FROM [sales].[InvoiceAttempts] WHERE [CommandName] = N'sales.invoice.match.v1')
                BEGIN
                    THROW 51225, 'Sales invoices were matched to official documents; AddSalesInvoiceOfficialMatch is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "InvoiceMatches",
                schema: "sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceAttempts_Command",
                schema: "sales",
                table: "InvoiceAttempts",
                sql: "[CommandName] IN (N'sales.invoice.create.v1', N'sales.invoice.submit.v1', N'sales.invoice.approve.v1', N'sales.invoice.reject.v1', N'sales.invoice.cancel.v1')");
        }
    }
}
