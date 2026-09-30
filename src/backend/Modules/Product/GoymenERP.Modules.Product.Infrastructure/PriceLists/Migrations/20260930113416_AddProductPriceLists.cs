using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <summary>
    /// AT-018: the company-scoped price lists of the Product module — lists, numbered validity-dated versions, their lines
    /// and the ADR-011 attempts — in the <c>product</c> schema, with this context's own history table. Additive only; the
    /// lines reference the Product card (<c>product.Products</c>) and the list its company (<c>organization.Companies</c>).
    /// <c>Down</c> refuses (<c>THROW 51063</c>, the price lists' own refusal, used by no other module on main or any remote
    /// branch at the time of writing) while any list or attempt exists — a published price is a recorded fact that orders
    /// may reference, so it is never silently dropped (forward fix); an empty schema rolls back freely.
    /// </summary>
    public partial class AddProductPriceLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "product");

            migrationBuilder.CreateTable(
                name: "PriceLists",
                schema: "product",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Turkish_100_CI_AS"),
                    CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    VatBasis = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceLists", x => x.Id);
                    table.UniqueConstraint("AK_PriceLists_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_PriceLists_Channel", "[Channel] = 1");
                    table.CheckConstraint("CK_PriceLists_Currency", "LEN([CurrencyCode]) = 3");
                    table.CheckConstraint("CK_PriceLists_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_PriceLists_Name", "LEN([Name]) > 0");
                    table.CheckConstraint("CK_PriceLists_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PriceLists_VatBasis", "[VatBasis] = 1");
                    table.CheckConstraint("CK_PriceLists_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_PriceLists_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceListAttempts",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusalJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceListAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_PriceListAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_PriceListAttempts_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                    table.CheckConstraint("CK_PriceListAttempts_Outcome", "([Outcome] = 1 AND [PriceListId] IS NOT NULL AND [PriceListId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) OR ([Outcome] = 2 AND [PriceListId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
                    table.ForeignKey(
                        name: "FK_PriceListAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceListAttempts_PriceLists_CompanyId_PriceListId",
                        columns: x => new { x.CompanyId, x.PriceListId },
                        principalSchema: "product",
                        principalTable: "PriceLists",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceListVersions",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceListVersions", x => new { x.CompanyId, x.PriceListId, x.VersionNo });
                    table.CheckConstraint("CK_PriceListVersions_Identity", "[VersionId] <> '00000000-0000-0000-0000-000000000000' AND [VersionNo] >= 1");
                    table.CheckConstraint("CK_PriceListVersions_State", "([State] = 1 AND [PublishedAtUtc] IS NULL AND [PublishedBy] IS NULL) OR ([State] = 2 AND [PublishedAtUtc] IS NOT NULL AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy]) > 0)");
                    table.CheckConstraint("CK_PriceListVersions_Text", "LEN([CreatedBy]) > 0");
                    table.CheckConstraint("CK_PriceListVersions_Validity", "[ValidThrough] IS NULL OR [ValidThrough] >= [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_PriceListVersions_PriceLists_CompanyId_PriceListId",
                        columns: x => new { x.CompanyId, x.PriceListId },
                        principalSchema: "product",
                        principalTable: "PriceLists",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PriceListLines",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    LineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    FloorUnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceListLines", x => new { x.CompanyId, x.PriceListId, x.VersionNo, x.LineId });
                    table.CheckConstraint("CK_PriceListLines_Identity", "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [PriceUnitId] <> '00000000-0000-0000-0000-000000000000' AND [LineNo] >= 1");
                    table.CheckConstraint("CK_PriceListLines_Price", "[UnitPrice] > 0 AND ([FloorUnitPrice] IS NULL OR ([FloorUnitPrice] > 0 AND [FloorUnitPrice] <= [UnitPrice]))");
                    table.ForeignKey(
                        name: "FK_PriceListLines_PriceListVersions_CompanyId_PriceListId_VersionNo",
                        columns: x => new { x.CompanyId, x.PriceListId, x.VersionNo },
                        principalSchema: "product",
                        principalTable: "PriceListVersions",
                        principalColumns: new[] { "CompanyId", "PriceListId", "VersionNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PriceListLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_PriceListAttempts_PriceListId",
                schema: "product",
                table: "PriceListAttempts",
                columns: new[] { "CompanyId", "PriceListId" },
                unique: true,
                filter: "[PriceListId] IS NOT NULL AND [CommandName] = N'product.price-list.create.v1'");

            migrationBuilder.CreateIndex(
                name: "IX_PriceListLines_CompanyId_ProductId",
                schema: "product",
                table: "PriceListLines",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceListLines_LineNo",
                schema: "product",
                table: "PriceListLines",
                columns: new[] { "CompanyId", "PriceListId", "VersionNo", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceListLines_ProductId",
                schema: "product",
                table: "PriceListLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceLists_CompanyId_UpdatedAtUtc_Id",
                schema: "product",
                table: "PriceLists",
                columns: new[] { "CompanyId", "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_PriceLists_CompanyId_Name",
                schema: "product",
                table: "PriceLists",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PriceListVersions_OneDraft",
                schema: "product",
                table: "PriceListVersions",
                columns: new[] { "CompanyId", "PriceListId" },
                unique: true,
                filter: "[State] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_PriceListVersions_PublishedValidFrom",
                schema: "product",
                table: "PriceListVersions",
                columns: new[] { "CompanyId", "PriceListId", "ValidFrom" },
                unique: true,
                filter: "[State] = 2");

            migrationBuilder.CreateIndex(
                name: "UX_PriceListVersions_VersionId",
                schema: "product",
                table: "PriceListVersions",
                columns: new[] { "CompanyId", "VersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [product].[PriceLists]) OR EXISTS (SELECT 1 FROM [product].[PriceListAttempts])
                    THROW 51063, 'Product price lists exist; AddProductPriceLists is not rolled back (forward fix).', 1;
                """);

            migrationBuilder.DropTable(
                name: "PriceListAttempts",
                schema: "product");

            migrationBuilder.DropTable(
                name: "PriceListLines",
                schema: "product");

            migrationBuilder.DropTable(
                name: "PriceListVersions",
                schema: "product");

            migrationBuilder.DropTable(
                name: "PriceLists",
                schema: "product");
        }
    }
}
