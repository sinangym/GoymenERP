using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPaintMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaintMembershipAttempts",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaintMembershipAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_PaintMembershipAttempts_State", "LEN([CommandName]) > 0 AND LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson], OBJECT) = 1 AND [StatusCode] IN (200,400,404,409,422) AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0");
                    table.ForeignKey(
                        name: "FK_PaintMembershipAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaintMembershipVersions",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SourceKind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    SourceLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    FileSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaintMembershipVersions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_PaintMembershipVersions_State", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND [SourceKind] IN ('Import','Edit') AND LEN([SourceLabel]) > 0 AND LEN([Reason]) > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND (([SourceKind] = 'Import' AND [FileName] IS NOT NULL AND LEN([FileName]) > 0 AND LEN([FileSha256]) = 64 AND [FileSha256] IS NOT NULL) OR ([SourceKind] = 'Edit' AND [FileName] IS NULL AND [FileSha256] IS NULL))");
                    table.ForeignKey(
                        name: "FK_PaintMembershipVersions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaintMembershipLines",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipVersion = table.Column<long>(type: "bigint", nullable: false),
                    RecordedProductVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaintMembershipLines", x => new { x.CompanyId, x.Version, x.ProductId });
                    table.CheckConstraint("CK_PaintMembershipLines_State", "[ProductId] <> '00000000-0000-0000-0000-000000000000' AND [MembershipVersion] >= 1 AND [MembershipVersion] <= [Version] AND [RecordedProductVersion] >= 1");
                    table.ForeignKey(
                        name: "FK_PaintMembershipLines_PaintMembershipVersions_CompanyId_MembershipVersion",
                        columns: x => new { x.CompanyId, x.MembershipVersion },
                        principalSchema: "product",
                        principalTable: "PaintMembershipVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaintMembershipLines_PaintMembershipVersions_CompanyId_Version",
                        columns: x => new { x.CompanyId, x.Version },
                        principalSchema: "product",
                        principalTable: "PaintMembershipVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaintMembershipLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaintMembershipLines_CompanyId_MembershipVersion",
                schema: "product",
                table: "PaintMembershipLines",
                columns: new[] { "CompanyId", "MembershipVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_PaintMembershipLines_CompanyId_ProductId_Version",
                schema: "product",
                table: "PaintMembershipLines",
                columns: new[] { "CompanyId", "ProductId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_PaintMembershipLines_ProductId",
                schema: "product",
                table: "PaintMembershipLines",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [product].[PaintMembershipVersions]) OR EXISTS (SELECT 1 FROM [product].[PaintMembershipLines]) OR EXISTS (SELECT 1 FROM [product].[PaintMembershipAttempts]) BEGIN THROW 51975, 'Paint membership source, history or command answers exist; AddProductPaintMemberships is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.DropTable(
                name: "PaintMembershipAttempts",
                schema: "product");

            migrationBuilder.DropTable(
                name: "PaintMembershipLines",
                schema: "product");

            migrationBuilder.DropTable(
                name: "PaintMembershipVersions",
                schema: "product");
        }
    }
}
