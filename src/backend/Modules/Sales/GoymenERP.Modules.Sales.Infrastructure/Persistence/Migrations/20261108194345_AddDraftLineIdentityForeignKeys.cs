using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftLineIdentityForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Review finding: the line-bound history rows referenced their line by position only, so a stored LineId could name
            // another line than the one at its LineNo. The new foreign keys bind the pair; before they are added, refuse (51234,
            // used by no other migration) while any over-delivery approval, line closure or remainder change already names a
            // line identity that is not the line at its position. Such rows are not repaired here: which side is right is a data
            // decision, never a migration's guess. Consistent or empty tables migrate freely.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [sales].[DraftOverDeliveryApprovals] AS [c] WHERE NOT EXISTS (SELECT 1 FROM [sales].[DraftLines] AS [l]
                        WHERE [l].[CompanyId] = [c].[CompanyId] AND [l].[DraftId] = [c].[DraftId] AND [l].[LineNo] = [c].[LineNo] AND [l].[LineId] = [c].[LineId]))
                    OR EXISTS (SELECT 1 FROM [sales].[DraftLineClosures] AS [c] WHERE NOT EXISTS (SELECT 1 FROM [sales].[DraftLines] AS [l]
                        WHERE [l].[CompanyId] = [c].[CompanyId] AND [l].[DraftId] = [c].[DraftId] AND [l].[LineNo] = [c].[LineNo] AND [l].[LineId] = [c].[LineId]))
                    OR EXISTS (SELECT 1 FROM [sales].[DraftRemainderChanges] AS [c] WHERE NOT EXISTS (SELECT 1 FROM [sales].[DraftLines] AS [l]
                        WHERE [l].[CompanyId] = [c].[CompanyId] AND [l].[DraftId] = [c].[DraftId] AND [l].[LineNo] = [c].[LineNo] AND [l].[LineId] = [c].[LineId]))
                BEGIN
                    THROW 51234, 'A line-bound sales history row names a LineId that is not the line at its LineNo; AddDraftLineIdentityForeignKeys is not applied until the data is corrected.', 1;
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_DraftRemainderChanges_CompanyId_DraftId_LineNo",
                schema: "sales",
                table: "DraftRemainderChanges");

            migrationBuilder.DropIndex(
                name: "IX_DraftOverDeliveryApprovals_CompanyId_DraftId_LineNo",
                schema: "sales",
                table: "DraftOverDeliveryApprovals");

            migrationBuilder.DropIndex(
                name: "IX_DraftLineClosures_CompanyId_DraftId_LineNo",
                schema: "sales",
                table: "DraftLineClosures");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DraftLines_LineIdentity",
                schema: "sales",
                table: "DraftLines",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftRemainderChanges_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftRemainderChanges",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftOverDeliveryApprovals_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftOverDeliveryApprovals",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftLineClosures_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftLineClosures",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DraftLineClosures_DraftLines_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftLineClosures",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" },
                principalSchema: "sales",
                principalTable: "DraftLines",
                principalColumns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DraftOverDeliveryApprovals_DraftLines_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftOverDeliveryApprovals",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" },
                principalSchema: "sales",
                principalTable: "DraftLines",
                principalColumns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DraftRemainderChanges_DraftLines_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftRemainderChanges",
                columns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" },
                principalSchema: "sales",
                principalTable: "DraftLines",
                principalColumns: new[] { "CompanyId", "DraftId", "LineNo", "LineId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down drops only constraints and indexes and recreates the position-only indexes: no row loses meaning, so no guard is
            // needed and the rollback is free (the position-only foreign keys stay in place throughout).
            migrationBuilder.DropForeignKey(
                name: "FK_DraftLineClosures_DraftLines_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftLineClosures");

            migrationBuilder.DropForeignKey(
                name: "FK_DraftOverDeliveryApprovals_DraftLines_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftOverDeliveryApprovals");

            migrationBuilder.DropForeignKey(
                name: "FK_DraftRemainderChanges_DraftLines_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftRemainderChanges");

            migrationBuilder.DropIndex(
                name: "IX_DraftRemainderChanges_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftRemainderChanges");

            migrationBuilder.DropIndex(
                name: "IX_DraftOverDeliveryApprovals_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftOverDeliveryApprovals");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DraftLines_LineIdentity",
                schema: "sales",
                table: "DraftLines");

            migrationBuilder.DropIndex(
                name: "IX_DraftLineClosures_CompanyId_DraftId_LineNo_LineId",
                schema: "sales",
                table: "DraftLineClosures");

            migrationBuilder.CreateIndex(
                name: "IX_DraftRemainderChanges_CompanyId_DraftId_LineNo",
                schema: "sales",
                table: "DraftRemainderChanges",
                columns: new[] { "CompanyId", "DraftId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftOverDeliveryApprovals_CompanyId_DraftId_LineNo",
                schema: "sales",
                table: "DraftOverDeliveryApprovals",
                columns: new[] { "CompanyId", "DraftId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftLineClosures_CompanyId_DraftId_LineNo",
                schema: "sales",
                table: "DraftLineClosures",
                columns: new[] { "CompanyId", "DraftId", "LineNo" });
        }
    }
}
