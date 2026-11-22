using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// WP15 (1.16): the duplicate check facts (invoice date, currency, payable) on the incoming invoice identity row, copied at
    /// recording from the immutable source and amounts, plus the supplier/date index. Pure DDL; no backfill: older rows keep
    /// nulls and the duplicate check reads their first revision snapshot. Down drops only derived copies (the revision snapshot
    /// stays the source of truth), so it needs no forward-fix guard and loses no information.
    /// </summary>
    public partial class AddPurchaseIncomingInvoiceDuplicateFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                schema: "purchasing",
                table: "IncomingInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InvoiceDate",
                schema: "purchasing",
                table: "IncomingInvoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PayableAmount",
                schema: "purchasing",
                table: "IncomingInvoices",
                type: "decimal(24,6)",
                precision: 24,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingInvoices_DuplicateDate",
                schema: "purchasing",
                table: "IncomingInvoices",
                columns: new[] { "CompanyId", "SupplierPartyId", "InvoiceDate" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingInvoices_DuplicateFacts",
                schema: "purchasing",
                table: "IncomingInvoices",
                sql: "([InvoiceDate] IS NULL AND [CurrencyId] IS NULL AND [PayableAmount] IS NULL) OR ([InvoiceDate] IS NOT NULL AND [CurrencyId] IS NOT NULL AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [PayableAmount] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncomingInvoices_DuplicateDate",
                schema: "purchasing",
                table: "IncomingInvoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingInvoices_DuplicateFacts",
                schema: "purchasing",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                schema: "purchasing",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "InvoiceDate",
                schema: "purchasing",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "PayableAmount",
                schema: "purchasing",
                table: "IncomingInvoices");
        }
    }
}
