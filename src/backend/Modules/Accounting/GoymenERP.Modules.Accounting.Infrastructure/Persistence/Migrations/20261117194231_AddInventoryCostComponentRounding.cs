using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCostComponentRounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals",
                sql: "([Status] IN (1,4,5) AND [Version] = 1 AND [PreparedBy] IS NULL AND [ApprovedBy] IS NULL AND [PeriodId] IS NULL AND [JournalId] IS NULL) OR ([Status] = 2 AND [Version] > 1 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NULL AND [JournalId] IS NULL) OR ([Status] IN (3,6) AND [Version] > 2 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND (([Status] = 3 AND [JournalId] IS NOT NULL) OR ([Status] = 6 AND [JournalId] IS NULL)) AND [ApprovedBy] <> [PreparedBy])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [accounting].[InventoryCostProposals]
                    WHERE [Status] = 6
                       OR JSON_QUERY([ViewJson], '$.preparation.rounding') IS NOT NULL
                       OR JSON_QUERY([ViewJson], '$.component.recordedRounding') IS NOT NULL)
                BEGIN
                    THROW 51803, 'Exact component rounding evidence exists; AddInventoryCostComponentRounding is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryCostProposals_State",
                schema: "accounting",
                table: "InventoryCostProposals",
                sql: "([Status] IN (1,4,5) AND [Version] = 1 AND [PreparedBy] IS NULL AND [ApprovedBy] IS NULL AND [PeriodId] IS NULL AND [JournalId] IS NULL) OR ([Status] = 2 AND [Version] > 1 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NULL AND [JournalId] IS NULL) OR ([Status] = 3 AND [Version] > 2 AND [PreparedBy] IS NOT NULL AND [PeriodId] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [JournalId] IS NOT NULL AND [ApprovedBy] <> [PreparedBy])");
        }
    }
}
