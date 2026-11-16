using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <summary>
    /// B-08 / BS-10 (STK-S12): the company's package standard table (pieces per package), one insert-only version per Excel import
    /// or ERP edit (<c>product.ProductPackageStandardVersions</c>) with the whole table in <c>product.ProductPackageStandardLines</c>
    /// (one row per product), and its command answers (<c>product.ProductPackageStandardAttempts</c>). No row is seeded: the read
    /// port answers NoSource until a table exists. <see cref="Down"/> is forward-fix: it refuses with THROW 51492 while a version or
    /// a command answer exists; an empty store rolls back freely.
    /// </summary>
    public partial class AddProductPackageStandards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductPackageStandardAttempts",
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
                    table.PrimaryKey("PK_ProductPackageStandardAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_ProductPackageStandardAttempts_Answer", "[StatusCode] BETWEEN 200 AND 599 AND ISJSON([AnswerJson]) = 1");
                    table.CheckConstraint("CK_ProductPackageStandardAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_ProductPackageStandardAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductPackageStandardVersions",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    FileSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    FileSizeBytes = table.Column<int>(type: "int", nullable: true),
                    EditedProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntryCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPackageStandardVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_ProductPackageStandardVersions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [VersionId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND [EntryCount] >= 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_ProductPackageStandardVersions_Source", "([SourceKind] = 'Import' AND [SourceLabel] IS NOT NULL AND LEN(LTRIM(RTRIM([SourceLabel]))) > 0 AND [FileName] IS NOT NULL AND LEN([FileName]) > 0 AND [FileSha256] IS NOT NULL AND LEN([FileSha256]) = 64 AND [FileSizeBytes] IS NOT NULL AND [FileSizeBytes] > 0 AND [EditedProductId] IS NULL AND [EntryCount] > 0) OR ([SourceKind] = 'Edit' AND [SourceLabel] IS NULL AND [FileName] IS NULL AND [FileSha256] IS NULL AND [FileSizeBytes] IS NULL AND [EditedProductId] IS NOT NULL AND [EditedProductId] <> '00000000-0000-0000-0000-000000000000')");
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardVersions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardVersions_Products_EditedProductId",
                        column: x => x.EditedProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductPackageStandardLines",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PiecesPerPackage = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPackageStandardLines", x => new { x.CompanyId, x.Version, x.ProductId });
                    table.CheckConstraint("CK_ProductPackageStandardLines_Values", "[ProductId] <> '00000000-0000-0000-0000-000000000000' AND [PiecesPerPackage] > 0");
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardLines_ProductPackageStandardVersions_CompanyId_Version",
                        columns: x => new { x.CompanyId, x.Version },
                        principalSchema: "product",
                        principalTable: "ProductPackageStandardVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackageStandardLines_ProductId",
                schema: "product",
                table: "ProductPackageStandardLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackageStandardVersions_CompanyId_VersionId",
                schema: "product",
                table: "ProductPackageStandardVersions",
                columns: new[] { "CompanyId", "VersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackageStandardVersions_EditedProductId",
                schema: "product",
                table: "ProductPackageStandardVersions",
                column: "EditedProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [product].[ProductPackageStandardVersions]) OR EXISTS (SELECT 1 FROM [product].[ProductPackageStandardAttempts]) BEGIN THROW 51492, 'Product package standard versions or command answers exist; AddProductPackageStandards is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "ProductPackageStandardAttempts",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductPackageStandardLines",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductPackageStandardVersions",
                schema: "product");
        }
    }
}
