using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations;

/// <summary>
/// The document vault's first schema (ERP-000G; DEC-DOC-001 Blob → Document → Version → Link; ADR-002, ADR-005,
/// ADR-009, ADR-011): the company's content blobs (one row per company and SHA-256 — physical dedup within the company,
/// DEC-DOC-002), the logical documents, their immutable versions with their own scan result (DEC-DOC-003/009), the
/// owner-record links and the ADR-011 attempt record. All are tenant tables keyed company-first; companies are
/// Organization's, referenced by key only, so this runs after Organization's migrations. The "one upload attempt per
/// document" index names its command in its filter. Nothing here is ever updated or deleted by the application.
/// <c>Down</c> refuses (<c>THROW 51058</c>, the vault's module refusal) while any document exists: a stored document
/// is never silently dropped while retention/deletion is undecided (OPEN-018/021; forward fix). An empty schema rolls
/// back freely.
/// </summary>
[DbContext(typeof(DocumentsDbContext))]
[Migration("20260930120000_InitialDocuments")]
public sealed class InitialDocuments : Migration
{
    private const string NotEmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "documents");

        migrationBuilder.CreateTable(
            name: "ContentBlobs",
            schema: "documents",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ContentKind = table.Column<int>(type: "int", nullable: false),
                ObjectKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false, collation: "Latin1_General_100_BIN2"),
                FirstStoredBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                FirstStoredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ContentBlobs", x => new { x.CompanyId, x.Sha256 });
                table.CheckConstraint("CK_ContentBlobs_Identity", "[CompanyId] <> " + NotEmptyGuid + " AND LEN([Sha256]) = 64 AND LEN([ObjectKey]) > 0 AND LEN([FirstStoredBy]) > 0");
                table.CheckConstraint("CK_ContentBlobs_Kind", "[ContentKind] IN (1, 2, 3)");
                table.CheckConstraint("CK_ContentBlobs_Size", "[SizeBytes] > 0");
                table.ForeignKey(
                    name: "FK_ContentBlobs_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Documents",
            schema: "documents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Documents", x => x.Id);
                table.UniqueConstraint("AK_Documents_CompanyId_Id", x => new { x.CompanyId, x.Id });
                table.CheckConstraint("CK_Documents_Identity", "[Id] <> " + NotEmptyGuid + " AND [CompanyId] <> " + NotEmptyGuid + " AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.ForeignKey(
                    name: "FK_Documents_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalSchema: "organization",
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DocumentAttempts",
            schema: "documents",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CommandName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false, collation: "Latin1_General_100_BIN2"),
                IdempotencyKeyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentAttempts", x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
                table.CheckConstraint("CK_DocumentAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
                table.CheckConstraint("CK_DocumentAttempts_Identity", "[CompanyId] <> " + NotEmptyGuid + " AND [DocumentId] <> " + NotEmptyGuid + " AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
                table.ForeignKey(
                    name: "FK_DocumentAttempts_Documents_CompanyId_DocumentId",
                    columns: x => new { x.CompanyId, x.DocumentId },
                    principalSchema: "documents",
                    principalTable: "Documents",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DocumentLinks",
            schema: "documents",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OwnerKind = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LinkedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                LinkedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentLinks", x => new { x.CompanyId, x.DocumentId, x.OwnerKind, x.OwnerId });
                table.CheckConstraint("CK_DocumentLinks_Identity", "[OwnerId] <> " + NotEmptyGuid + " AND LEN([OwnerKind]) > 0 AND LEN([LinkedBy]) > 0");
                table.ForeignKey(
                    name: "FK_DocumentLinks_Documents_CompanyId_DocumentId",
                    columns: x => new { x.CompanyId, x.DocumentId },
                    principalSchema: "documents",
                    principalTable: "Documents",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DocumentVersions",
            schema: "documents",
            columns: table => new
            {
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VersionNo = table.Column<int>(type: "int", nullable: false),
                FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                ScanState = table.Column<int>(type: "int", nullable: false),
                ScannerId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                UploadedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DocumentVersions", x => new { x.CompanyId, x.DocumentId, x.VersionNo });
                table.CheckConstraint("CK_DocumentVersions_Scan", "([ScanState] = 2 AND [ScannerId] IS NOT NULL AND LEN([ScannerId]) > 0) OR ([ScanState] = 1 AND [ScannerId] IS NULL)");
                table.CheckConstraint("CK_DocumentVersions_Text", "LEN([FileName]) > 0 AND LEN([UploadedBy]) > 0 AND LEN([CorrelationId]) > 0");
                table.CheckConstraint("CK_DocumentVersions_VersionNo", "[VersionNo] >= 1");
                table.ForeignKey(
                    name: "FK_DocumentVersions_ContentBlobs_CompanyId_Sha256",
                    columns: x => new { x.CompanyId, x.Sha256 },
                    principalSchema: "documents",
                    principalTable: "ContentBlobs",
                    principalColumns: new[] { "CompanyId", "Sha256" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DocumentVersions_Documents_CompanyId_DocumentId",
                    columns: x => new { x.CompanyId, x.DocumentId },
                    principalSchema: "documents",
                    principalTable: "Documents",
                    principalColumns: new[] { "CompanyId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "UX_DocumentAttempts_DocumentId",
            schema: "documents",
            table: "DocumentAttempts",
            columns: new[] { "CompanyId", "DocumentId" },
            unique: true,
            filter: "[CommandName] = N'documents.upload.v1'");

        migrationBuilder.CreateIndex(
            name: "IX_DocumentLinks_CompanyId_OwnerKind_OwnerId_LinkedAtUtc",
            schema: "documents",
            table: "DocumentLinks",
            columns: new[] { "CompanyId", "OwnerKind", "OwnerId", "LinkedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_DocumentVersions_CompanyId_Sha256_UploadedAtUtc",
            schema: "documents",
            table: "DocumentVersions",
            columns: new[] { "CompanyId", "Sha256", "UploadedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A document is a stored fact with its bytes in the object store; dropping these tables would orphan them silently
        // and erase their audit anchors. While any document exists the rollback is refused, before any drop, with the
        // vault's own module error (51058, used by no other migration on main or any branch at the time of writing); an
        // empty database rolls back freely. Forward fix, the Logistics 51056 pattern.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [documents].[Documents])
                THROW 51058, 'Documents exist; InitialDocuments is not rolled back (forward fix).', 1;
            """);

        migrationBuilder.DropTable(name: "DocumentAttempts", schema: "documents");
        migrationBuilder.DropTable(name: "DocumentLinks", schema: "documents");
        migrationBuilder.DropTable(name: "DocumentVersions", schema: "documents");
        migrationBuilder.DropTable(name: "ContentBlobs", schema: "documents");
        migrationBuilder.DropTable(name: "Documents", schema: "documents");
    }
}
