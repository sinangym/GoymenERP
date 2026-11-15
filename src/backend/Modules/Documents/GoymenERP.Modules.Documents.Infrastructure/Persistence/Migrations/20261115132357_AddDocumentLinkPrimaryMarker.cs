using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// KLN-D49 (4): the first link of a document is its primary link. Until now the primary link was only implied (the
    /// upload's link is the earliest one); this migration stores the marker on <c>DocumentLinks.IsPrimary</c>, marks the
    /// earliest existing link of every document as primary (the same order the store used to read: linked time, owner
    /// kind, owner id) and keeps at most one primary link per document with the filtered unique index
    /// <c>UX_DocumentLinks_Primary</c>. <c>Down</c> refuses (<c>THROW 51447</c>) while the marker says something the link
    /// order cannot: a primary link that is not its document's earliest link, or a linked document without a primary
    /// link. Otherwise the marker is derivable from the order again and the rollback drops it (forward fix). An empty
    /// table rolls back freely.
    /// </summary>
    public partial class AddDocumentLinkPrimaryMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                schema: "documents",
                table: "DocumentLinks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Backfill: the earliest link of every document (the upload's link) becomes its primary link, one per document.
            migrationBuilder.Sql("""
                WITH [Ranked] AS (
                    SELECT [IsPrimary],
                           ROW_NUMBER() OVER (
                               PARTITION BY [CompanyId], [DocumentId]
                               ORDER BY [LinkedAtUtc], [OwnerKind], [OwnerId]) AS [Position]
                    FROM [documents].[DocumentLinks])
                UPDATE [Ranked] SET [IsPrimary] = 1 WHERE [Position] = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_DocumentLinks_Primary",
                schema: "documents",
                table: "DocumentLinks",
                columns: new[] { "CompanyId", "DocumentId" },
                unique: true,
                filter: "[IsPrimary] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The guard runs before every drop: the marker is dropped only while it equals "the earliest link".
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM (
                        SELECT [IsPrimary],
                               ROW_NUMBER() OVER (
                                   PARTITION BY [CompanyId], [DocumentId]
                                   ORDER BY [LinkedAtUtc], [OwnerKind], [OwnerId]) AS [Position]
                        FROM [documents].[DocumentLinks]) AS [Ranked]
                    WHERE ([Position] = 1 AND [IsPrimary] = 0) OR ([Position] > 1 AND [IsPrimary] = 1))
                BEGIN
                    THROW 51447, 'A primary document link is not its document''s earliest link; AddDocumentLinkPrimaryMarker is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "UX_DocumentLinks_Primary",
                schema: "documents",
                table: "DocumentLinks");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                schema: "documents",
                table: "DocumentLinks");
        }
    }
}
