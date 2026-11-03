using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A1-2 (plan API-02; Üretim note A-2, Sales note S-A1, ALS-D16): a purchase request line's optional source — SourceKind
    /// (1 Stock, 2 Sales, 3 Production) with the source record's opaque identity (SourceDocumentId, SourceDocumentLineId).
    /// All three columns are nullable and every existing line stays manual (all null); no stored row is changed. A check keeps
    /// the three together: none (manual) or a defined kind with both identities non-empty. A filtered index serves the reads
    /// by source record (the Sales adaptor S-A1/MOD-03, the Üretim action A-2/U5-c).
    /// <para>
    /// Down refuses (<c>THROW 51167</c>, used by no other migration) while any line carries a source: a sourced line cannot be
    /// represented before this migration and dropping the columns would silently turn it manual (forward fix). Otherwise it
    /// drops the index, the check and the columns.
    /// </para>
    /// </summary>
    public partial class AddPurchaseRequestLineSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceDocumentId",
                schema: "purchasing",
                table: "PurchaseRequestLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceDocumentLineId",
                schema: "purchasing",
                table: "PurchaseRequestLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceKind",
                schema: "purchasing",
                table: "PurchaseRequestLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequestLines_Source",
                schema: "purchasing",
                table: "PurchaseRequestLines",
                columns: new[] { "CompanyId", "SourceKind", "SourceDocumentId" },
                filter: "[SourceKind] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseRequestLines_Source",
                schema: "purchasing",
                table: "PurchaseRequestLines",
                sql: "([SourceKind] IS NULL AND [SourceDocumentId] IS NULL AND [SourceDocumentLineId] IS NULL) OR ([SourceKind] IS NOT NULL AND [SourceKind] IN (1, 2, 3) AND [SourceDocumentId] IS NOT NULL AND [SourceDocumentId] <> '00000000-0000-0000-0000-000000000000' AND [SourceDocumentLineId] IS NOT NULL AND [SourceDocumentLineId] <> '00000000-0000-0000-0000-000000000000')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [purchasing].[PurchaseRequestLines]
                    WHERE [SourceKind] IS NOT NULL OR [SourceDocumentId] IS NOT NULL OR [SourceDocumentLineId] IS NOT NULL)
                BEGIN
                    THROW 51167, 'Purchase request lines with a source exist; AddPurchaseRequestLineSource is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequestLines_Source",
                schema: "purchasing",
                table: "PurchaseRequestLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseRequestLines_Source",
                schema: "purchasing",
                table: "PurchaseRequestLines");

            migrationBuilder.DropColumn(
                name: "SourceDocumentId",
                schema: "purchasing",
                table: "PurchaseRequestLines");

            migrationBuilder.DropColumn(
                name: "SourceDocumentLineId",
                schema: "purchasing",
                table: "PurchaseRequestLines");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                schema: "purchasing",
                table: "PurchaseRequestLines");
        }
    }
}
