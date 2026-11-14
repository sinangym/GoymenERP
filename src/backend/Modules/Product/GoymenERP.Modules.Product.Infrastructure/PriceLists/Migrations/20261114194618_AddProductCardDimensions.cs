using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <summary>
    /// BS-9 (2026-11-11): the "Ebat/kalınlık" row of the shared product card (<c>product.ProductCardDimensions</c>, one row per
    /// product, the section shape held by <c>CK_ProductCardDimensions_Shape</c>) and its company-scoped command answers
    /// (<c>product.ProductDimensionAttempts</c>). <see cref="Down"/> is forward-fix: it refuses with THROW 51290 while any
    /// dimension row or command answer exists; an empty store rolls back freely.
    /// </summary>
    public partial class AddProductCardDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductCardDimensions",
                schema: "product",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionKind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ThicknessMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    WidthMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    LengthMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    HeightMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    OuterDiameterMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    WallThicknessMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    KgPerMetre = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedInCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCardDimensions", x => x.ProductId);
                    table.CheckConstraint("CK_ProductCardDimensions_Identity", "[ProductId] <> '00000000-0000-0000-0000-000000000000' AND [UpdatedInCompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ProductCardDimensions_Measures", "([ThicknessMm] IS NULL OR [ThicknessMm] > 0) AND ([WidthMm] IS NULL OR [WidthMm] > 0) AND ([LengthMm] IS NULL OR [LengthMm] > 0) AND ([HeightMm] IS NULL OR [HeightMm] > 0) AND ([OuterDiameterMm] IS NULL OR [OuterDiameterMm] > 0) AND ([WallThicknessMm] IS NULL OR [WallThicknessMm] > 0) AND ([KgPerMetre] IS NULL OR [KgPerMetre] > 0)");
                    table.CheckConstraint("CK_ProductCardDimensions_Shape", "([SectionKind] = 'Sheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'Coil' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'RoundTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'Profile' AND [ThicknessMm] IS NULL AND [LengthMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [KgPerMetre] IS NOT NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm])");
                    table.CheckConstraint("CK_ProductCardDimensions_Version", "[Version] >= 1 AND LEN([UpdatedBy]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductCardDimensions_Companies_UpdatedInCompanyId",
                        column: x => x.UpdatedInCompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductCardDimensions_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductDimensionAttempts",
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
                    table.PrimaryKey("PK_ProductDimensionAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_ProductDimensionAttempts_Answer", "[StatusCode] BETWEEN 200 AND 599 AND ISJSON([AnswerJson]) = 1");
                    table.CheckConstraint("CK_ProductDimensionAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_ProductDimensionAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductDimensionAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCardDimensions_UpdatedInCompanyId",
                schema: "product",
                table: "ProductCardDimensions",
                column: "UpdatedInCompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [product].[ProductCardDimensions]) OR EXISTS (SELECT 1 FROM [product].[ProductDimensionAttempts]) BEGIN THROW 51290, 'Product card dimensions exist; AddProductCardDimensions is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "ProductCardDimensions",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductDimensionAttempts",
                schema: "product");
        }
    }
}
