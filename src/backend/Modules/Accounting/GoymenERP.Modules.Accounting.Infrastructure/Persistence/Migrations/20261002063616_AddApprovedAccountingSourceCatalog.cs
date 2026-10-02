using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovedAccountingSourceCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovedSourceCatalogs",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: true),
                    TemplateVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountPlanSourceVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountsSourceSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PostingMappingsSourceSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PackageJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActivatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovedSourceCatalogs", x => x.Id);
                    table.UniqueConstraint("AK_ApprovedSourceCatalogs_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_ApprovedSourceCatalogs_Hashes", "LEN([AccountsSourceSha256]) = 64 AND LEN([PostingMappingsSourceSha256]) = 64");
                    table.CheckConstraint("CK_ApprovedSourceCatalogs_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PreparedByUserId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ApprovedSourceCatalogs_Status", "([Status] = 1 AND [CatalogVersion] IS NULL AND [ActivatedByUserId] IS NULL AND [ActivatedAtUtc] IS NULL) OR ([Status] IN (2, 3) AND [CatalogVersion] >= 1 AND [ActivatedByUserId] IS NOT NULL AND [ActivatedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ActivatedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_ApprovedSourceCatalogs_Text", "LEN([TemplateVersion]) > 0 AND LEN([AccountPlanSourceVersion]) > 0 AND ISJSON([PackageJson]) = 1");
                    table.CheckConstraint("CK_ApprovedSourceCatalogs_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_ApprovedSourceCatalogs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ApprovedSourceCatalogs_Active",
                schema: "accounting",
                table: "ApprovedSourceCatalogs",
                columns: new[] { "CompanyId", "Status" },
                unique: true,
                filter: "[Status] = 2");

            migrationBuilder.CreateIndex(
                name: "UX_ApprovedSourceCatalogs_Version",
                schema: "accounting",
                table: "ApprovedSourceCatalogs",
                columns: new[] { "CompanyId", "CatalogVersion" },
                unique: true,
                filter: "[CatalogVersion] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [accounting].[ApprovedSourceCatalogs])
                BEGIN
                    ;THROW 51049, N'Accounting approved source catalog migration cannot roll back while catalog history exists.', 1;
                END;
                """);

            migrationBuilder.DropTable(
                name: "ApprovedSourceCatalogs",
                schema: "accounting");
        }
    }
}
