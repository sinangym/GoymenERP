using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCogsClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CogsClassifications",
                schema: "product",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecordedProductVersion = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    OriginCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityRevision = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CogsClassifications", x => new { x.ProductId, x.Version });
                    table.CheckConstraint("CK_CogsClassifications_Identity", "[ProductId] <> '00000000-0000-0000-0000-000000000000' AND [OriginCompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ActorId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_CogsClassifications_Kind", "[Kind] IN (1,2)");
                    table.CheckConstraint("CK_CogsClassifications_Provenance", "LEN([AuthorityRevision]) > 0 AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
                    table.CheckConstraint("CK_CogsClassifications_Version", "[Version] >= 1 AND [RecordedProductVersion] >= 1");
                    table.ForeignKey(
                        name: "FK_CogsClassifications_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [product].[CogsClassifications])
                BEGIN
                    THROW 51978, 'Explicit COGS classification evidence exists; AddProductCogsClassifications is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "CogsClassifications",
                schema: "product");
        }
    }
}
