using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryOutboxCursorAndExactCostRatios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostStamps_Currency",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostStamps_Value",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.AddColumn<byte[]>(
                name: "Position",
                schema: "inventory",
                table: "OutboxMessages",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "ForeignUnitCostRatioJson",
                schema: "inventory",
                table: "MovementCostStamps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PoolEffectJson",
                schema: "inventory",
                table: "MovementCostStamps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitCostRatioJson",
                schema: "inventory",
                table: "MovementCostStamps",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CompanyId_Position",
                schema: "inventory",
                table: "OutboxMessages",
                columns: new[] { "CompanyId", "Position" });

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementCostStamps_Value",
                schema: "inventory",
                table: "MovementCostStamps",
                sql: "([Status] = 1 AND (([UnitCostPerKg] IS NOT NULL AND [Amount] IS NOT NULL) OR [UnitCostRatioJson] IS NOT NULL) AND [LedgerVersion] >= 1 AND [LedgerVersion] IS NOT NULL AND [Reason] IS NULL) OR ([Status] = 2 AND [UnitCostPerKg] IS NULL AND [Amount] IS NULL AND [UnitCostRatioJson] IS NULL AND LEN([Reason]) > 0 AND [Reason] IS NOT NULL AND ([LedgerVersion] IS NULL OR [LedgerVersion] >= 1))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[OutboxMessages])
                    THROW 51958, 'Inventory outbox cursor provenance exists; forward fix is required.', 1;
                """);
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [inventory].[MovementCostStamps]
                    WHERE [UnitCostRatioJson] IS NOT NULL OR [ForeignUnitCostRatioJson] IS NOT NULL OR [PoolEffectJson] IS NOT NULL)
                    THROW 51964, 'Inventory exact movement cost provenance exists; forward fix is required.', 1;
                """);
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CompanyId_Position",
                schema: "inventory",
                table: "OutboxMessages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostStamps_Currency",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostStamps_ExactProof",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MovementCostStamps_Value",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropColumn(
                name: "Position",
                schema: "inventory",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ForeignUnitCostRatioJson",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropColumn(
                name: "PoolEffectJson",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.DropColumn(
                name: "UnitCostRatioJson",
                schema: "inventory",
                table: "MovementCostStamps");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementCostStamps_Currency",
                schema: "inventory",
                table: "MovementCostStamps",
                sql: "LEN([CurrencySourceVersion]) > 0 AND (([EntryCurrencyStatus] = 1 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL AND [EntryExchangeRate] IS NULL AND [CurrencyReason] IS NULL) OR ([EntryCurrencyStatus] = 2 AND [OriginalCurrencyId] IS NOT NULL AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [ForeignUnitCostPerKg] <> '0' AND [ForeignUnitCostPerKg] IS NOT NULL AND [EntryExchangeRate] > 0 AND [EntryExchangeRate] IS NOT NULL AND [CurrencyReason] IS NULL) OR ([EntryCurrencyStatus] = 3 AND [OriginalCurrencyId] IS NOT NULL AND [OriginalCurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [EntryExchangeRate] IS NULL AND ([ForeignUnitCostPerKg] IS NULL OR [ForeignUnitCostPerKg] <> '0') AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL) OR ([EntryCurrencyStatus] = 4 AND [OriginalCurrencyId] IS NULL AND [ForeignUnitCostPerKg] IS NULL AND [EntryExchangeRate] IS NULL AND LEN([CurrencyReason]) > 0 AND [CurrencyReason] IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MovementCostStamps_Value",
                schema: "inventory",
                table: "MovementCostStamps",
                sql: "([Status] = 1 AND [UnitCostPerKg] IS NOT NULL AND [Amount] IS NOT NULL AND [LedgerVersion] >= 1 AND [LedgerVersion] IS NOT NULL AND [Reason] IS NULL) OR ([Status] = 2 AND [UnitCostPerKg] IS NULL AND [Amount] IS NULL AND LEN([Reason]) > 0 AND [Reason] IS NOT NULL AND ([LedgerVersion] IS NULL OR [LedgerVersion] >= 1))");
        }
    }
}
