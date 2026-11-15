using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <summary>
    /// URN-D11…D16 (STK-S37R): the company's kg/m table imported from a source-labelled file (<c>product.ProductProfileWeightImports</c>,
    /// one insert-only version per import linked to its vault document, with its rows in <c>product.ProductProfileWeightLines</c>, one
    /// row per measure pair) and its command answers (<c>product.ProductProfileWeightAttempts</c>). The card's profile branch of
    /// <c>CK_ProductCardDimensions_Shape</c> no longer holds a typed kg/m: a profile takes it from the table. <see cref="Down"/> is
    /// forward-fix: it refuses with THROW 51483 while an import, a command answer or a profile card without a stored kg/m exists
    /// (the old check could not hold it); an empty store rolls back freely.
    /// </summary>
    public partial class AddProductProfileWeightImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.CreateTable(
                name: "ProductProfileWeightAttempts",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    AnswerJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductProfileWeightAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_ProductProfileWeightAttempts_Answer", "[StatusCode] BETWEEN 200 AND 599 AND ISJSON([AnswerJson]) = 1");
                    table.CheckConstraint("CK_ProductProfileWeightAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_ProductProfileWeightAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductProfileWeightAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductProfileWeightImports",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    FileSizeBytes = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductProfileWeightImports", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_ProductProfileWeightImports_File", "LEN([FileSha256]) = 64 AND [FileSizeBytes] > 0 AND [RowCount] > 0");
                    table.CheckConstraint("CK_ProductProfileWeightImports_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ImportId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND LEN([ImportedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_ProductProfileWeightImports_Source", "LEN(LTRIM(RTRIM([SourceLabel]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([FileName]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductProfileWeightImports_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductProfileWeightLines",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportVersion = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    SectionKind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WidthMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    HeightMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    OuterDiameterMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    WallThicknessMm = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    KgPerMetre = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductProfileWeightLines", x => new { x.CompanyId, x.ImportVersion, x.LineNo });
                    table.CheckConstraint("CK_ProductProfileWeightLines_Shape", "([SectionKind] = 'Profile' AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL AND [WidthMm] > 0 AND [HeightMm] > 0 AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm]) OR ([SectionKind] = 'RoundTube' AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [OuterDiameterMm] > 0 AND 2 * [WallThicknessMm] <= [OuterDiameterMm])");
                    table.CheckConstraint("CK_ProductProfileWeightLines_Values", "[LineNo] >= 1 AND [WallThicknessMm] > 0 AND [KgPerMetre] > 0");
                    table.ForeignKey(
                        name: "FK_ProductProfileWeightLines_ProductProfileWeightImports_CompanyId_ImportVersion",
                        columns: x => new { x.CompanyId, x.ImportVersion },
                        principalSchema: "product",
                        principalTable: "ProductProfileWeightImports",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([SectionKind] = 'Sheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'Coil' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'RoundTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'Profile' AND [ThicknessMm] IS NULL AND [LengthMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm])");

            migrationBuilder.CreateIndex(
                name: "IX_ProductProfileWeightImports_CompanyId_ImportId",
                schema: "product",
                table: "ProductProfileWeightImports",
                columns: new[] { "CompanyId", "ImportId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductProfileWeightLines_Measures",
                schema: "product",
                table: "ProductProfileWeightLines",
                columns: new[] { "CompanyId", "ImportVersion", "SectionKind", "WidthMm", "HeightMm", "OuterDiameterMm", "WallThicknessMm" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [product].[ProductProfileWeightImports]) OR EXISTS (SELECT 1 FROM [product].[ProductProfileWeightAttempts]) OR EXISTS (SELECT 1 FROM [product].[ProductCardDimensions] WHERE [SectionKind] = 'Profile' AND [KgPerMetre] IS NULL) BEGIN THROW 51483, 'Product kg/m table imports or profile cards without a stored kg/m exist; AddProductProfileWeightImports is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "ProductProfileWeightAttempts",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductProfileWeightLines",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductProfileWeightImports",
                schema: "product");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductCardDimensions_Shape",
                schema: "product",
                table: "ProductCardDimensions",
                sql: "([SectionKind] = 'Sheet' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NOT NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'Coil' AND [ThicknessMm] IS NOT NULL AND [WidthMm] IS NOT NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WallThicknessMm] IS NULL AND [KgPerMetre] IS NULL) OR ([SectionKind] = 'RoundTube' AND [ThicknessMm] IS NULL AND [WidthMm] IS NULL AND [LengthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) OR ([SectionKind] = 'Profile' AND [ThicknessMm] IS NULL AND [LengthMm] IS NULL AND [OuterDiameterMm] IS NULL AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [WallThicknessMm] IS NOT NULL AND [KgPerMetre] IS NOT NULL AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm])");
        }
    }
}
