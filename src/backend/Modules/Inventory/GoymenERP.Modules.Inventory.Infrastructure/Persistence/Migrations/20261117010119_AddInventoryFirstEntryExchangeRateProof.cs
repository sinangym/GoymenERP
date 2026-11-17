using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryFirstEntryExchangeRateProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostStamps_Currency",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostStamps_ExactProof",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.AddColumn<string>(
                name: "EntryExchangeRateRatioJson",
                schema: "inventory",
                table: "MovementCostStamps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstEntryRateJson",
                schema: "inventory",
                table: "MovementCostStamps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementCostStamps_Currency",
                schema: "inventory",
                table: "MovementCostStamps",
                sql: "LEN([CurrencySourceVersion]) > 0 AND (([EntryCurrencyStatus] = 1 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL AND [EntryExchangeRate] IS NULL AND [EntryExchangeRateRatioJson] IS NULL AND [ForeignUnitCostRatioJson] IS NULL AND [CurrencyReason] IS NULL) OR ([EntryCurrencyStatus] = 2 AND [OriginalCurrencyId] IS NOT NULL AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND (([ForeignUnitCostPerKg] <> '0' AND [ForeignUnitCostPerKg] IS NOT NULL) OR [ForeignUnitCostRatioJson] IS NOT NULL) AND (([EntryExchangeRate] > 0 AND [EntryExchangeRate] IS NOT NULL) OR [EntryExchangeRateRatioJson] IS NOT NULL) AND [CurrencyReason] IS NULL) OR ([EntryCurrencyStatus] = 3 AND [OriginalCurrencyId] IS NOT NULL AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryExchangeRate] IS NULL AND [EntryExchangeRateRatioJson] IS NULL AND ([ForeignUnitCostPerKg] IS NULL OR [ForeignUnitCostPerKg] <> '0') AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL) OR ([EntryCurrencyStatus] = 4 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL AND [EntryExchangeRate] IS NULL AND [EntryExchangeRateRatioJson] IS NULL AND [ForeignUnitCostRatioJson] IS NULL AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementCostStamps_ExactProof",
                schema: "inventory",
                table: "MovementCostStamps",
                sql: "([UnitCostRatioJson] IS NULL OR ISJSON([UnitCostRatioJson], OBJECT) = 1) AND ([ForeignUnitCostRatioJson] IS NULL OR ISJSON([ForeignUnitCostRatioJson], OBJECT) = 1) AND ([PoolEffectJson] IS NULL OR ISJSON([PoolEffectJson], OBJECT) = 1) AND ([EntryExchangeRateRatioJson] IS NULL OR ISJSON([EntryExchangeRateRatioJson], OBJECT) = 1) AND ([FirstEntryRateJson] IS NULL OR ISJSON([FirstEntryRateJson], OBJECT) = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[MovementCostStamps]
                    WHERE [FirstEntryRateJson] IS NOT NULL OR [EntryExchangeRateRatioJson] IS NOT NULL)
                BEGIN
                    THROW 51972, 'Immutable first-entry or exact issue FX proof exists; AddInventoryFirstEntryExchangeRateProof is not rolled back (forward fix).', 1;
                END;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostStamps_Currency",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostStamps_ExactProof",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropColumn(
                name: "EntryExchangeRateRatioJson",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropColumn(
                name: "FirstEntryRateJson",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementCostStamps_Currency",
                schema: "inventory",
                table: "MovementCostStamps",
                sql: "LEN([CurrencySourceVersion]) > 0 AND (([EntryCurrencyStatus] = 1 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL AND [EntryExchangeRate] IS NULL AND [CurrencyReason] IS NULL) OR ([EntryCurrencyStatus] = 2 AND [OriginalCurrencyId] IS NOT NULL AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND (([ForeignUnitCostPerKg] <> '0' AND [ForeignUnitCostPerKg] IS NOT NULL) OR [ForeignUnitCostRatioJson] IS NOT NULL) AND [EntryExchangeRate] > 0 AND [EntryExchangeRate] IS NOT NULL AND [CurrencyReason] IS NULL) OR ([EntryCurrencyStatus] = 3 AND [OriginalCurrencyId] IS NOT NULL AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryExchangeRate] IS NULL AND ([ForeignUnitCostPerKg] IS NULL OR [ForeignUnitCostPerKg] <> '0') AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL) OR ([EntryCurrencyStatus] = 4 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL AND [EntryExchangeRate] IS NULL AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementCostStamps_ExactProof",
                schema: "inventory",
                table: "MovementCostStamps",
                sql: "([UnitCostRatioJson] IS NULL OR ISJSON([UnitCostRatioJson], OBJECT) = 1) AND ([ForeignUnitCostRatioJson] IS NULL OR ISJSON([ForeignUnitCostRatioJson], OBJECT) = 1) AND ([PoolEffectJson] IS NULL OR ISJSON([PoolEffectJson], OBJECT) = 1)");
        }
    }
}
