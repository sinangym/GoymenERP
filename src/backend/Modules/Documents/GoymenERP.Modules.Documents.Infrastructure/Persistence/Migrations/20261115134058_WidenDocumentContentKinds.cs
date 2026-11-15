using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// B-26 / FNS-D52: the content blob's kind check takes the bank-statement import kinds — XLSX (4), SWIFT MT940 (5) and
    /// CSV (6) — next to PDF, PNG and JPEG (1–3). Only the check changes; no row is touched and no column is added. Which
    /// owner record accepts the new kinds is its owner port's opt-in; no existing owner accepts them.
    /// <para>
    /// Down refuses (<c>THROW 51517</c>, used by no other migration) while any blob of a bank-statement kind exists:
    /// narrowing the check would strand stored documents the vault still serves (forward fix). Otherwise it restores the
    /// PDF/PNG/JPEG check.
    /// </para>
    /// </summary>
    public partial class WidenDocumentContentKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ContentBlobs_Kind",
                schema: "documents",
                table: "ContentBlobs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContentBlobs_Kind",
                schema: "documents",
                table: "ContentBlobs",
                sql: "[ContentKind] IN (1, 2, 3, 4, 5, 6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [documents].[ContentBlobs] WHERE [ContentKind] IN (4, 5, 6))
                BEGIN
                    THROW 51517, 'Bank-statement documents exist; WidenDocumentContentKinds is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_ContentBlobs_Kind",
                schema: "documents",
                table: "ContentBlobs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContentBlobs_Kind",
                schema: "documents",
                table: "ContentBlobs",
                sql: "[ContentKind] IN (1, 2, 3)");
        }
    }
}
