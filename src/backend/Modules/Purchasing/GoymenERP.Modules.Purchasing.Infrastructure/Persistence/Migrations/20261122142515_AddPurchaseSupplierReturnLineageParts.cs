using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// AK-8b (SAT-D433/SAT-D440/SAT-D442, K3-B2): the per-receipt parts and the piece count of a supplier return that leaves a
    /// foreign source label. Down refuses with THROW 52072 while any return carries parts or a piece count (its per-receipt
    /// exits, the preparer's invoiced/pending choice and its pieces would be lost); an empty store rolls back freely.
    /// </summary>
    public partial class AddPurchaseSupplierReturnLineageParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LineagePartsJson",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PieceCount",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseSupplierReturns_LineageParts",
                schema: "purchasing",
                table: "PurchaseSupplierReturns",
                sql: "([LineagePartsJson] IS NULL OR ([SourceLabelId] IS NOT NULL AND ISJSON([LineagePartsJson]) = 1)) AND ([PieceCount] IS NULL OR ([SourceLabelId] IS NOT NULL AND [PieceCount] >= 1))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseSupplierReturns] WHERE [LineagePartsJson] IS NOT NULL OR [PieceCount] IS NOT NULL)
                BEGIN
                    THROW 52072, 'Supplier return per-receipt parts or piece counts exist; AddPurchaseSupplierReturnLineageParts is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseSupplierReturns_LineageParts",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.DropColumn(
                name: "LineagePartsJson",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");

            migrationBuilder.DropColumn(
                name: "PieceCount",
                schema: "purchasing",
                table: "PurchaseSupplierReturns");
        }
    }
}
