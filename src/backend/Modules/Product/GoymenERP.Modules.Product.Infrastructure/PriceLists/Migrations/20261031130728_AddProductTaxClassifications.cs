using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <summary>
    /// MOD-03 (SAT-D73/D74/D82): the company's product tax classification (VAT class, optional withholding class) and its
    /// command answers. <c>Down</c> refuses (<c>THROW 51132</c>, used by no other migration on main or any remote branch at
    /// the time of writing) while any classification or command answer exists — dropping them would silently turn every
    /// classified product's tax back into "kaynak yok" and lose the recorded answers (forward fix); on an empty store the
    /// tables drop freely.
    /// </summary>
    public partial class AddProductTaxClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductTaxClassificationAttempts",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    AnswerJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTaxClassificationAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_ProductTaxClassificationAttempts_Answer", "[StatusCode] BETWEEN 200 AND 599 AND ISJSON([AnswerJson]) = 1");
                    table.CheckConstraint("CK_ProductTaxClassificationAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_ProductTaxClassificationAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductTaxClassificationAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductTaxClassifications",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VatClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WithholdingClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTaxClassifications", x => new { x.CompanyId, x.ProductId });
                    table.CheckConstraint("CK_ProductTaxClassifications_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [VatClassId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ProductTaxClassifications_Version", "[Version] >= 1 AND LEN([UpdatedBy]) > 0");
                    table.CheckConstraint("CK_ProductTaxClassifications_Withholding", "[WithholdingClassId] IS NULL OR ([WithholdingClassId] <> '00000000-0000-0000-0000-000000000000' AND [WithholdingClassId] <> [VatClassId])");
                    table.ForeignKey(
                        name: "FK_ProductTaxClassifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductTaxClassifications_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductTaxClassifications_ProductId",
                schema: "product",
                table: "ProductTaxClassifications",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [product].[ProductTaxClassifications])
                    OR EXISTS (SELECT 1 FROM [product].[ProductTaxClassificationAttempts])
                    THROW 51132, 'Product tax classifications exist; AddProductTaxClassifications is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "ProductTaxClassificationAttempts",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductTaxClassifications",
                schema: "product");
        }
    }
}
