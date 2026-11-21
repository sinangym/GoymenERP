using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentBrandingPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplatePublicationId",
                schema: "documents",
                table: "DocumentVersions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplatePublicationJson",
                schema: "documents",
                table: "DocumentVersions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyBrandingRevisions",
                schema: "documents",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LogoDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LogoVersionNo = table.Column<int>(type: "int", nullable: true),
                    LetterheadDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LetterheadVersionNo = table.Column<int>(type: "int", nullable: true),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyBrandingRevisions", x => new { x.CompanyId, x.Version });
                    table.CheckConstraint("CK_CompanyBrandingRevisions_Letterhead", "([LetterheadDocumentId] IS NULL AND [LetterheadVersionNo] IS NULL) OR ([LetterheadDocumentId] IS NOT NULL AND [LetterheadDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [LetterheadVersionNo] IS NOT NULL AND [LetterheadVersionNo] >= 1)");
                    table.CheckConstraint("CK_CompanyBrandingRevisions_Logo", "([LogoDocumentId] IS NULL AND [LogoVersionNo] IS NULL) OR ([LogoDocumentId] IS NOT NULL AND [LogoDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [LogoVersionNo] IS NOT NULL AND [LogoVersionNo] >= 1)");
                    table.CheckConstraint("CK_CompanyBrandingRevisions_Text", "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_CompanyBrandingRevisions_Version", "[Version] >= 1");
                    table.ForeignKey(
                        name: "FK_CompanyBrandingRevisions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyBrandingRevisions_DocumentVersions_CompanyId_LetterheadDocumentId_LetterheadVersionNo",
                        columns: x => new { x.CompanyId, x.LetterheadDocumentId, x.LetterheadVersionNo },
                        principalSchema: "documents",
                        principalTable: "DocumentVersions",
                        principalColumns: new[] { "CompanyId", "DocumentId", "VersionNo" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyBrandingRevisions_DocumentVersions_CompanyId_LogoDocumentId_LogoVersionNo",
                        columns: x => new { x.CompanyId, x.LogoDocumentId, x.LogoVersionNo },
                        principalSchema: "documents",
                        principalTable: "DocumentVersions",
                        principalColumns: new[] { "CompanyId", "DocumentId", "VersionNo" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentBrandingAttempts",
                schema: "documents",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentBrandingAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                    table.CheckConstraint("CK_DocumentBrandingAttempts_Command", "[CommandName] IN ('documents.company-branding.set.v1','documents.template.prepare.v1','documents.template.publish.v1')");
                    table.CheckConstraint("CK_DocumentBrandingAttempts_Identity", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0");
                    table.CheckConstraint("CK_DocumentBrandingAttempts_Result", "([Status] = 200 AND [Code] IS NULL AND [ResultJson] IS NOT NULL AND ISJSON([ResultJson]) = 1) OR ([Status] = 409 AND [Code] IS NOT NULL AND LEN([Code]) > 0 AND [ResultJson] IS NULL)");
                    table.ForeignKey(
                        name: "FK_DocumentBrandingAttempts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "organization",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTemplateDrafts",
                schema: "documents",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<int>(type: "int", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    BrandingVersion = table.Column<long>(type: "bigint", nullable: false),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PreparedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplateDrafts", x => new { x.CompanyId, x.Module, x.TemplateCode, x.Version });
                    table.UniqueConstraint("AK_DocumentTemplateDrafts_CompanyId_Module_TemplateCode_Version_BrandingVersion_PreparedBy", x => new { x.CompanyId, x.Module, x.TemplateCode, x.Version, x.BrandingVersion, x.PreparedBy });
                    table.CheckConstraint("CK_DocumentTemplateDrafts_Module", "([Module] = 1 AND [TemplateCode] = 'sales.order-pdf') OR ([Module] = 2 AND [TemplateCode] = 'inventory.material-label.technical') OR ([Module] = 3 AND [TemplateCode] = 'logistics.dispatch-note')");
                    table.CheckConstraint("CK_DocumentTemplateDrafts_Text", "LEN(LTRIM(RTRIM([PreparedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_DocumentTemplateDrafts_Versions", "[Version] >= 1 AND [BrandingVersion] >= 1");
                    table.ForeignKey(
                        name: "FK_DocumentTemplateDrafts_CompanyBrandingRevisions_CompanyId_BrandingVersion",
                        columns: x => new { x.CompanyId, x.BrandingVersion },
                        principalSchema: "documents",
                        principalTable: "CompanyBrandingRevisions",
                        principalColumns: new[] { "CompanyId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTemplatePublications",
                schema: "documents",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<int>(type: "int", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftVersion = table.Column<long>(type: "bigint", nullable: false),
                    BrandingVersion = table.Column<long>(type: "bigint", nullable: false),
                    PreparedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PublishedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplatePublications", x => new { x.CompanyId, x.Module, x.TemplateCode, x.Version });
                    table.UniqueConstraint("AK_DocumentTemplatePublications_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.CheckConstraint("CK_DocumentTemplatePublications_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 AND [DraftVersion] >= 1 AND [BrandingVersion] >= 1");
                    table.CheckConstraint("CK_DocumentTemplatePublications_MakerChecker", "[PreparedBy] <> [PublishedBy] AND LEN(LTRIM(RTRIM([PublishedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_DocumentTemplatePublications_Module", "([Module] = 1 AND [TemplateCode] = 'sales.order-pdf') OR ([Module] = 2 AND [TemplateCode] = 'inventory.material-label.technical') OR ([Module] = 3 AND [TemplateCode] = 'logistics.dispatch-note')");
                    table.ForeignKey(
                        name: "FK_DocumentTemplatePublications_DocumentTemplateDrafts_CompanyId_Module_TemplateCode_DraftVersion_BrandingVersion_PreparedBy",
                        columns: x => new { x.CompanyId, x.Module, x.TemplateCode, x.DraftVersion, x.BrandingVersion, x.PreparedBy },
                        principalSchema: "documents",
                        principalTable: "DocumentTemplateDrafts",
                        principalColumns: new[] { "CompanyId", "Module", "TemplateCode", "Version", "BrandingVersion", "PreparedBy" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_CompanyId_TemplatePublicationId",
                schema: "documents",
                table: "DocumentVersions",
                columns: new[] { "CompanyId", "TemplatePublicationId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentVersions_Publication",
                schema: "documents",
                table: "DocumentVersions",
                sql: "([TemplatePublicationId] IS NULL AND [TemplatePublicationJson] IS NULL) OR ([TemplatePublicationId] IS NOT NULL AND [TemplatePublicationId] <> '00000000-0000-0000-0000-000000000000' AND [TemplatePublicationJson] IS NOT NULL AND ISJSON([TemplatePublicationJson]) = 1 AND [ScanState] = 3)");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBrandingRevisions_CompanyId_LetterheadDocumentId_LetterheadVersionNo",
                schema: "documents",
                table: "CompanyBrandingRevisions",
                columns: new[] { "CompanyId", "LetterheadDocumentId", "LetterheadVersionNo" });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyBrandingRevisions_CompanyId_LogoDocumentId_LogoVersionNo",
                schema: "documents",
                table: "CompanyBrandingRevisions",
                columns: new[] { "CompanyId", "LogoDocumentId", "LogoVersionNo" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplateDrafts_CompanyId_BrandingVersion",
                schema: "documents",
                table: "DocumentTemplateDrafts",
                columns: new[] { "CompanyId", "BrandingVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplatePublications_CompanyId_Id",
                schema: "documents",
                table: "DocumentTemplatePublications",
                columns: new[] { "CompanyId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplatePublications_CompanyId_Module_TemplateCode_DraftVersion",
                schema: "documents",
                table: "DocumentTemplatePublications",
                columns: new[] { "CompanyId", "Module", "TemplateCode", "DraftVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplatePublications_CompanyId_Module_TemplateCode_DraftVersion_BrandingVersion_PreparedBy",
                schema: "documents",
                table: "DocumentTemplatePublications",
                columns: new[] { "CompanyId", "Module", "TemplateCode", "DraftVersion", "BrandingVersion", "PreparedBy" });

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentVersions_DocumentTemplatePublications_CompanyId_TemplatePublicationId",
                schema: "documents",
                table: "DocumentVersions",
                columns: new[] { "CompanyId", "TemplatePublicationId" },
                principalSchema: "documents",
                principalTable: "DocumentTemplatePublications",
                principalColumns: new[] { "CompanyId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [documents].[CompanyBrandingRevisions])
                    OR EXISTS (SELECT 1 FROM [documents].[DocumentTemplateDrafts])
                    OR EXISTS (SELECT 1 FROM [documents].[DocumentTemplatePublications])
                    OR EXISTS (SELECT 1 FROM [documents].[DocumentBrandingAttempts])
                    OR EXISTS (SELECT 1 FROM [documents].[DocumentVersions]
                        WHERE [TemplatePublicationId] IS NOT NULL OR [TemplatePublicationJson] IS NOT NULL)
                BEGIN
                    THROW 51936, 'Document branding or publication evidence exists; AddDocumentBrandingPublications is not rolled back (forward fix).', 1;
                END;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentVersions_DocumentTemplatePublications_CompanyId_TemplatePublicationId",
                schema: "documents",
                table: "DocumentVersions");

            migrationBuilder.DropTable(
                name: "DocumentBrandingAttempts",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "DocumentTemplatePublications",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "DocumentTemplateDrafts",
                schema: "documents");

            migrationBuilder.DropTable(
                name: "CompanyBrandingRevisions",
                schema: "documents");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_CompanyId_TemplatePublicationId",
                schema: "documents",
                table: "DocumentVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentVersions_Publication",
                schema: "documents",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "TemplatePublicationId",
                schema: "documents",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "TemplatePublicationJson",
                schema: "documents",
                table: "DocumentVersions");
        }
    }
}
