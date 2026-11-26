using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaleCostProposalClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals");

            migrationBuilder.AddColumn<byte[]>(
                name: "OutboxPosition",
                schema: "accounting",
                table: "InventoryCostProposals",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCostProposals_CompanyId_OutboxPosition",
                schema: "accounting",
                table: "InventoryCostProposals",
                columns: new[] { "CompanyId", "OutboxPosition" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryCostProposals_StaleClosure",
                schema: "accounting",
                table: "InventoryCostProposals",
                columns: new[] { "CompanyId", "Id" },
                unique: true,
                filter: "[Status] = 7");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals",
                sql: "([Status] IN (1,4,5) AND [Version] = 1 AND [PreparedBy] IS NULL AND [ApprovedBy] IS NULL AND [PeriodId] IS NULL AND [JournalId] IS NULL) OR ([Status] = 2 AND [Version] > 1 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NULL AND [JournalId] IS NULL) OR ([Status] IN (3,6) AND [Version] > 2 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND (([Status] = 3 AND [JournalId] IS NOT NULL) OR ([Status] = 6 AND [JournalId] IS NULL)) AND [ApprovedBy] <> [PreparedBy]) OR ([Status] = 7 AND [Version] > 2 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NULL AND [JournalId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[InventoryCostProposals] WHERE [Status] = 7)
                    THROW 52140, 'Stale proposal closure evidence exists; forward fix required.', 1;
                IF EXISTS (SELECT 1 FROM [accounting].[InventoryCostProposals] WHERE JSON_QUERY([ViewJson], '$.renewedFrom') IS NOT NULL)
                    THROW 52141, 'Fresh proposal lineage exists; forward fix required.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_InventoryCostProposals_CompanyId_OutboxPosition",
                schema: "accounting",
                table: "InventoryCostProposals");

            migrationBuilder.DropIndex(
                name: "UX_InventoryCostProposals_StaleClosure",
                schema: "accounting",
                table: "InventoryCostProposals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals");

            migrationBuilder.DropColumn(
                name: "OutboxPosition",
                schema: "accounting",
                table: "InventoryCostProposals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals",
                sql: "([Status] IN (1,4,5) AND [Version] = 1 AND [PreparedBy] IS NULL AND [ApprovedBy] IS NULL AND [PeriodId] IS NULL AND [JournalId] IS NULL) OR ([Status] = 2 AND [Version] > 1 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NULL AND [JournalId] IS NULL) OR ([Status] IN (3,6) AND [Version] > 2 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND (([Status] = 3 AND [JournalId] IS NOT NULL) OR ([Status] = 6 AND [JournalId] IS NULL)) AND [ApprovedBy] <> [PreparedBy])");
        }
    }
}
