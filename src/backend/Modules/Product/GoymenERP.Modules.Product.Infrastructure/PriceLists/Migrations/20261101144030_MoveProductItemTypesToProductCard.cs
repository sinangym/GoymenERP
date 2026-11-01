using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <summary>
    /// SAT-D149 (EK20, <c>docs/decisions/SINAN_KARARLARI_2026-11-01_SATIS_EK20.md</c>): the item type moves from the company-scoped
    /// <c>product.ProductItemTypes</c> (PR #866) to the SHARED product card (<c>product.ProductCardItemTypes</c>, keyed by the
    /// product). Forward fix: <c>Up</c> refuses (<c>THROW 51151</c>) when one product carries different item types in different
    /// companies — it never chooses — otherwise it moves the agreed value (version 1, the latest change's actor, time and company)
    /// and drops the old table. <c>Down</c> refuses (<c>THROW 51152</c>) while any shared item type exists (which company would
    /// own it is unknowable); on an empty store it restores the empty company-scoped table. Both numbers are used by no other
    /// migration on main or any remote branch at the time of writing. The command answers table is unchanged.
    /// </summary>
    public partial class MoveProductItemTypesToProductCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SAT-D149: never silently choose between companies — a product typed differently in two companies stops the move.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT [ProductId] FROM [product].[ProductItemTypes]
                    GROUP BY [ProductId] HAVING COUNT(DISTINCT [ItemType]) > 1)
                    THROW 51151, 'A product has different item types in different companies; MoveProductItemTypesToProductCard refuses to choose one (resolve the rows first).', 1;
                """);

            migrationBuilder.CreateTable(
                name: "ProductCardItemTypes",
                schema: "product",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedInCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCardItemTypes", x => x.ProductId);
                    table.CheckConstraint("CK_ProductCardItemTypes_Identity", "[ProductId] <> '00000000-0000-0000-0000-000000000000' AND [UpdatedInCompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ProductCardItemTypes_ItemType", "[ItemType] IN ('Steel', 'Scrap', 'Service')");
                    table.CheckConstraint("CK_ProductCardItemTypes_Version", "[Version] >= 1 AND LEN([UpdatedBy]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductCardItemTypes_Companies_UpdatedInCompanyId",
                        column: x => x.UpdatedInCompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCardItemTypes_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCardItemTypes_UpdatedInCompanyId",
                schema: "product",
                table: "ProductCardItemTypes",
                column: "UpdatedInCompanyId");

            // The agreed value moves to the shared card as its first version; the latest change names the acting company.
            migrationBuilder.Sql("""
                INSERT INTO [product].[ProductCardItemTypes]
                    ([ProductId], [ItemType], [Version], [UpdatedAtUtc], [UpdatedBy], [UpdatedInCompanyId])
                SELECT [ProductId], [ItemType], 1, [UpdatedAtUtc], [UpdatedBy], [CompanyId]
                FROM (
                    SELECT [ProductId], [ItemType], [UpdatedAtUtc], [UpdatedBy], [CompanyId],
                        ROW_NUMBER() OVER (PARTITION BY [ProductId] ORDER BY [UpdatedAtUtc] DESC, [CompanyId]) AS [Latest]
                    FROM [product].[ProductItemTypes]) AS [Rows]
                WHERE [Latest] = 1;
                """);

            migrationBuilder.DropTable(
                name: "ProductItemTypes",
                schema: "product");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [product].[ProductCardItemTypes])
                    THROW 51152, 'Shared product card item types exist; MoveProductItemTypesToProductCard is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "ProductCardItemTypes",
                schema: "product");

            migrationBuilder.CreateTable(
                name: "ProductItemTypes",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemType = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductItemTypes", x => new { x.CompanyId, x.ProductId });
                    table.CheckConstraint("CK_ProductItemTypes_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ProductItemTypes_ItemType", "[ItemType] IN ('Steel', 'Scrap', 'Service')");
                    table.CheckConstraint("CK_ProductItemTypes_Version", "[Version] >= 1 AND LEN([UpdatedBy]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductItemTypes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductItemTypes_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductItemTypes_ProductId",
                schema: "product",
                table: "ProductItemTypes",
                column: "ProductId");
        }
    }
}
