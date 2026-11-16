using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations
{
    /// <summary>
    /// Adds package-standard approval decisions, customer override lines and kept import files. Import files are append-only
    /// (trigger, THROW 51871). <see cref="Down"/> is forward-fix: it refuses with THROW 51872 while any decision, customer line,
    /// kept file or approval-required version exists.
    /// </summary>
    public partial class AddProductPackageStandardApprovalAndFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Identity",
                schema: "product",
                table: "ProductPackageStandardVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Source",
                schema: "product",
                table: "ProductPackageStandardVersions");

            migrationBuilder.AddColumn<int>(
                name: "CustomerEntryCount",
                schema: "product",
                table: "ProductPackageStandardVersions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "EditedCustomerId",
                schema: "product",
                table: "ProductPackageStandardVersions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresApproval",
                schema: "product",
                table: "ProductPackageStandardVersions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ProductPackageStandardCustomerLines",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PiecesPerPackage = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPackageStandardCustomerLines", x => new { x.CompanyId, x.Version, x.CustomerId, x.ProductId });
                    table.CheckConstraint("CK_ProductPackageStandardCustomerLines_Values", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [CustomerId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [PiecesPerPackage] > 0");
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardCustomerLines_ProductPackageStandardVersions_CompanyId_Version",
                        columns: x => new { x.CompanyId, x.Version },
                        principalSchema: "product",
                        principalTable: "ProductPackageStandardVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardCustomerLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "product",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductPackageStandardDecisions",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Decision = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPackageStandardDecisions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_ProductPackageStandardDecisions_Decision", "([Decision] = 'Approved' AND [EffectiveFrom] IS NOT NULL) OR ([Decision] = 'Rejected' AND [EffectiveFrom] IS NULL)");
                    table.CheckConstraint("CK_ProductPackageStandardDecisions_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DecisionId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND LEN([DecidedBy]) > 0 AND LEN([CorrelationId]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardDecisions_ProductPackageStandardVersions_CompanyId_Version",
                        columns: x => new { x.CompanyId, x.Version },
                        principalSchema: "product",
                        principalTable: "ProductPackageStandardVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductPackageStandardImportFiles",
                schema: "product",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    FileSizeBytes = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProblemCode = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: true),
                    SourceLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPackageStandardImportFiles", x => new { x.CompanyId, x.ImportId });
                    table.CheckConstraint("CK_ProductPackageStandardImportFiles_File", "LEN([FileName]) > 0 AND LEN([FileSha256]) = 64 AND [FileSizeBytes] > 0 AND [FileSizeBytes] <= 2097152 AND DATALENGTH([Content]) = [FileSizeBytes] AND ISJSON([ReportJson]) = 1");
                    table.CheckConstraint("CK_ProductPackageStandardImportFiles_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ImportId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN(LTRIM(RTRIM([SourceLabel]))) > 0");
                    table.CheckConstraint("CK_ProductPackageStandardImportFiles_Outcome", "([Outcome] = 'Accepted' AND [Version] IS NOT NULL AND [ProblemCode] IS NULL) OR ([Outcome] = 'Refused' AND [Version] IS NULL AND [ProblemCode] IS NOT NULL AND LEN([ProblemCode]) > 0)");
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardImportFiles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPackageStandardImportFiles_ProductPackageStandardVersions_CompanyId_Version",
                        columns: x => new { x.CompanyId, x.Version },
                        principalSchema: "product",
                        principalTable: "ProductPackageStandardVersions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""CREATE TRIGGER [product].[TR_ProductPackageStandardImportFiles_Guard] ON [product].[ProductPackageStandardImportFiles] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51871, 'Package standard import files are append-only.', 1; END;""");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Approval",
                schema: "product",
                table: "ProductPackageStandardVersions",
                sql: "[RequiresApproval] = 1 OR ([CustomerEntryCount] = 0 AND [EditedCustomerId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Identity",
                schema: "product",
                table: "ProductPackageStandardVersions",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [VersionId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND [EntryCount] >= 0 AND [CustomerEntryCount] >= 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Source",
                schema: "product",
                table: "ProductPackageStandardVersions",
                sql: "([SourceKind] = 'Import' AND [SourceLabel] IS NOT NULL AND LEN(LTRIM(RTRIM([SourceLabel]))) > 0 AND [FileName] IS NOT NULL AND LEN([FileName]) > 0 AND [FileSha256] IS NOT NULL AND LEN([FileSha256]) = 64 AND [FileSizeBytes] IS NOT NULL AND [FileSizeBytes] > 0 AND [EditedProductId] IS NULL AND [EditedCustomerId] IS NULL AND [EntryCount] > 0) OR ([SourceKind] = 'Edit' AND [SourceLabel] IS NULL AND [FileName] IS NULL AND [FileSha256] IS NULL AND [FileSizeBytes] IS NULL AND [EditedProductId] IS NOT NULL AND [EditedProductId] <> '00000000-0000-0000-0000-000000000000' AND ([EditedCustomerId] IS NULL OR [EditedCustomerId] <> '00000000-0000-0000-0000-000000000000'))");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackageStandardCustomerLines_ProductId",
                schema: "product",
                table: "ProductPackageStandardCustomerLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackageStandardDecisions_CompanyId_DecisionId",
                schema: "product",
                table: "ProductPackageStandardDecisions",
                columns: new[] { "CompanyId", "DecisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackageStandardImportFiles_CompanyId_CreatedAtUtc",
                schema: "product",
                table: "ProductPackageStandardImportFiles",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackageStandardImportFiles_CompanyId_Version",
                schema: "product",
                table: "ProductPackageStandardImportFiles",
                columns: new[] { "CompanyId", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""IF EXISTS (SELECT 1 FROM [product].[ProductPackageStandardImportFiles]) OR EXISTS (SELECT 1 FROM [product].[ProductPackageStandardDecisions]) OR EXISTS (SELECT 1 FROM [product].[ProductPackageStandardCustomerLines]) OR EXISTS (SELECT 1 FROM [product].[ProductPackageStandardVersions] WHERE [RequiresApproval] = 1) BEGIN THROW 51872, 'Package standard approvals, customer overrides or kept files exist; AddProductPackageStandardApprovalAndFiles is not rolled back (forward fix).', 1; END;""");

            migrationBuilder.Sql("DROP TRIGGER [product].[TR_ProductPackageStandardImportFiles_Guard];");

            migrationBuilder.DropTable(
                name: "ProductPackageStandardCustomerLines",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductPackageStandardDecisions",
                schema: "product");

            migrationBuilder.DropTable(
                name: "ProductPackageStandardImportFiles",
                schema: "product");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Approval",
                schema: "product",
                table: "ProductPackageStandardVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Identity",
                schema: "product",
                table: "ProductPackageStandardVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Source",
                schema: "product",
                table: "ProductPackageStandardVersions");

            migrationBuilder.DropColumn(
                name: "CustomerEntryCount",
                schema: "product",
                table: "ProductPackageStandardVersions");

            migrationBuilder.DropColumn(
                name: "EditedCustomerId",
                schema: "product",
                table: "ProductPackageStandardVersions");

            migrationBuilder.DropColumn(
                name: "RequiresApproval",
                schema: "product",
                table: "ProductPackageStandardVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Identity",
                schema: "product",
                table: "ProductPackageStandardVersions",
                sql: "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [VersionId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND [EntryCount] >= 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductPackageStandardVersions_Source",
                schema: "product",
                table: "ProductPackageStandardVersions",
                sql: "([SourceKind] = 'Import' AND [SourceLabel] IS NOT NULL AND LEN(LTRIM(RTRIM([SourceLabel]))) > 0 AND [FileName] IS NOT NULL AND LEN([FileName]) > 0 AND [FileSha256] IS NOT NULL AND LEN([FileSha256]) = 64 AND [FileSizeBytes] IS NOT NULL AND [FileSizeBytes] > 0 AND [EditedProductId] IS NULL AND [EntryCount] > 0) OR ([SourceKind] = 'Edit' AND [SourceLabel] IS NULL AND [FileName] IS NULL AND [FileSha256] IS NULL AND [FileSizeBytes] IS NULL AND [EditedProductId] IS NOT NULL AND [EditedProductId] <> '00000000-0000-0000-0000-000000000000')");
        }
    }
}
