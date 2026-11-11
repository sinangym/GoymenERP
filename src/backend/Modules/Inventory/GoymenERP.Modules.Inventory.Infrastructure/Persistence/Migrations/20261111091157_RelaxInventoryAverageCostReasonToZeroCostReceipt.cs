using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelaxInventoryAverageCostReasonToZeroCostReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AverageCostLedger_State",
                schema: "inventory",
                table: "AverageCostLedger");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AverageCostLedger_State",
                schema: "inventory",
                table: "AverageCostLedger",
                sql: "[Version] >= 1 AND (([Status] = 1 AND [UnreadableReason] IS NULL AND [QuantityKg] IS NOT NULL AND [QuantityKg] >= 0 AND [AverageCostPerKg] IS NOT NULL AND [AverageCostPerKg] >= 0) OR ([Status] = 2 AND [UnreadableReason] IS NOT NULL AND [UnreadableReason] BETWEEN 1 AND 9 AND [QuantityKg] IS NULL AND [AverageCostPerKg] IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[AverageCostLedger] WHERE [UnreadableReason] = 9)
                BEGIN
                    THROW 51258, 'Inventory average cost ledger rows with zero_cost_receipt exist; RelaxInventoryAverageCostReasonToZeroCostReceipt is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_AverageCostLedger_State",
                schema: "inventory",
                table: "AverageCostLedger");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AverageCostLedger_State",
                schema: "inventory",
                table: "AverageCostLedger",
                sql: "[Version] >= 1 AND (([Status] = 1 AND [UnreadableReason] IS NULL AND [QuantityKg] IS NOT NULL AND [QuantityKg] >= 0 AND [AverageCostPerKg] IS NOT NULL AND [AverageCostPerKg] >= 0) OR ([Status] = 2 AND [UnreadableReason] IS NOT NULL AND [UnreadableReason] BETWEEN 1 AND 8 AND [QuantityKg] IS NULL AND [AverageCostPerKg] IS NULL))");
        }
    }
}
