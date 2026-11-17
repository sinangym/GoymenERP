using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialSalesInvoiceSourceDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "InvoiceDate",
                schema: "finance",
                table: "OfficialSalesInvoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OfficialSalesInvoices_InvoiceDate",
                schema: "finance",
                table: "OfficialSalesInvoices",
                sql: "[InvoiceDate] IS NULL OR [InvoiceDate] > '00010101'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [finance].[OfficialSalesInvoices] WHERE [InvoiceDate] IS NOT NULL)
                    THROW 51965, 'Official invoice source dates exist; use a forward fix.', 1;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_OfficialSalesInvoices_InvoiceDate",
                schema: "finance",
                table: "OfficialSalesInvoices");

            migrationBuilder.DropColumn(
                name: "InvoiceDate",
                schema: "finance",
                table: "OfficialSalesInvoices");
        }
    }
}
