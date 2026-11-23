using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderDocumentNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DocumentNumberCancelledAtUtc",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DocumentNumberFormatVersion",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DocumentNumberSequence",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CompanyId_DocumentNumber",
                schema: "purchasing",
                table: "PurchaseOrders",
                columns: new[] { "CompanyId", "DocumentNumber" },
                unique: true,
                filter: "[DocumentNumber] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseOrders_DocumentNumber",
                schema: "purchasing",
                table: "PurchaseOrders",
                sql: "([DocumentNumber] IS NULL AND [DocumentNumberFormatVersion] IS NULL AND [DocumentNumberSequence] IS NULL AND [DocumentNumberCancelledAtUtc] IS NULL) OR ([DocumentNumber] IS NOT NULL AND LEN([DocumentNumber]) > 0 AND [DocumentNumberFormatVersion] IS NOT NULL AND [DocumentNumberFormatVersion] > 0 AND [DocumentNumberSequence] IS NOT NULL AND [DocumentNumberSequence] > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseOrders] WHERE [DocumentNumber] IS NOT NULL
                    OR [DocumentNumberFormatVersion] IS NOT NULL OR [DocumentNumberSequence] IS NOT NULL OR [DocumentNumberCancelledAtUtc] IS NOT NULL)
                BEGIN
                    THROW 51957, 'Purchase order number history exists; AddPurchaseOrderDocumentNumbers is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_CompanyId_DocumentNumber",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseOrders_DocumentNumber",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DocumentNumberCancelledAtUtc",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DocumentNumberFormatVersion",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DocumentNumberSequence",
                schema: "purchasing",
                table: "PurchaseOrders");
        }
    }
}
