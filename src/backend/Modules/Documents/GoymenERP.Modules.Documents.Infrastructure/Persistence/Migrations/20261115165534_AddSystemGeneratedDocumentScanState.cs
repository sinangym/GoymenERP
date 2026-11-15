using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// KLN-D60: a version may be <c>SystemGenerated</c> (3) — a PDF the server itself produced through the generated-document
    /// port, kept without a malware scan and therefore naming no scanner, with its SHA-256 as every version. Only the scan
    /// check changes; no row is touched and no column is added.
    /// <para>
    /// Down refuses (<c>THROW 51592</c>, used by no other migration) while any system-generated version exists: narrowing
    /// the check would strand stored documents the vault still serves (forward fix). Otherwise it restores the
    /// Clean/NotScanned check.
    /// </para>
    /// </summary>
    public partial class AddSystemGeneratedDocumentScanState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentVersions_Scan",
                schema: "documents",
                table: "DocumentVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentVersions_Scan",
                schema: "documents",
                table: "DocumentVersions",
                sql: "([ScanState] = 2 AND [ScannerId] IS NOT NULL AND LEN([ScannerId]) > 0) OR ([ScanState] = 1 AND [ScannerId] IS NULL) OR ([ScanState] = 3 AND [ScannerId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [documents].[DocumentVersions] WHERE [ScanState] = 3)
                BEGIN
                    THROW 51592, 'System-generated document versions exist; AddSystemGeneratedDocumentScanState is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentVersions_Scan",
                schema: "documents",
                table: "DocumentVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentVersions_Scan",
                schema: "documents",
                table: "DocumentVersions",
                sql: "([ScanState] = 2 AND [ScannerId] IS NOT NULL AND LEN([ScannerId]) > 0) OR ([ScanState] = 1 AND [ScannerId] IS NULL)");
        }
    }
}
