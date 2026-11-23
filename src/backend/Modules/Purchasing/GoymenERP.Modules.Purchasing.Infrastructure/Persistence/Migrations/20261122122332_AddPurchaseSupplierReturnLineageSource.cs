using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// AK-8 (SAT-D433/SAT-D441/SAT-D445): the foreign source label and mode of a supplier return. Down refuses with THROW 52071
    /// while any return names a source label (its physical exit would lose its label); an empty store rolls back freely.
    /// </summary>
    public partial class AddPurchaseSupplierReturnLineageSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LineageMode",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceLabelId",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseSupplierReturns_Lineage",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                sql: "([SourceLabelId] IS NULL AND [LineageMode] IS NULL) OR ([SourceLabelId] IS NOT NULL AND [SourceLabelId] <> '00000000-0000-0000-0000-000000000000' AND [LineageMode] IS NOT NULL AND [LineageMode] IN (1, 2))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseSupplierReturns] WHERE [SourceLabelId] IS NOT NULL)
                BEGIN
                    THROW 52071, 'Supplier return source label history exists; AddPurchaseSupplierReturnLineageSource is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseSupplierReturns_Lineage",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.DropColumn(
                name: "LineageMode",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.DropColumn(
                name: "SourceLabelId",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");
        }
    }
}
