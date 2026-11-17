using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Shared projection of an ACTUAL immutable movement stamp; no money/FX fallback or rounding.</summary>
internal static class InventoryMovementCostStampProjection
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static InventoryMovementCostStampV1 Read(MovementCostStampRow? row, decimal kg, Guid currency)
    {
        if (row is null) return new(InventoryMovementCostStampStatusV1.Unavailable, currency, null, null, null,
            "legacy-not-recorded", "movement_cost_stamp_not_recorded");
        if (row.StampVersion < 1 || row.CurrencyId != currency) throw new InvalidOperationException();
        if (row.Status == (int)InventoryMovementCostStampStatusV1.Known && row.LedgerVersion is >= 1 && row.Reason is null)
        {
            var exact = row.UnitCostRatioJson is { } json ? (JsonSerializer.Deserialize<InventoryExactCostRatioV1>(json, Json)
                ?? throw new InvalidOperationException()).Value() : GoymenERP.Modules.Inventory.Domain.Costing.ExactCostRatio.Of(
                    row.UnitCostPerKg ?? throw new InvalidOperationException());
            if (exact.Sign < 0 || row.UnitCostPerKg is { } scalar && (!exact.TryToDecimal(out var actual) || actual != scalar)
                || row.Amount is { } amount && (!exact.Multiply(kg).TryToDecimal(out var actualAmount) || actualAmount != amount))
                throw new InvalidOperationException();
            return new(InventoryMovementCostStampStatusV1.Known, currency, row.UnitCostPerKg, row.Amount, row.LedgerVersion,
                row.StampVersion.ToString(CultureInfo.InvariantCulture), null, InventoryExactCostRatioV1.From(exact));
        }
        if (row.Status == (int)InventoryMovementCostStampStatusV1.Unavailable && row.UnitCostPerKg is null && row.Amount is null
            && !string.IsNullOrWhiteSpace(row.Reason))
            return new(InventoryMovementCostStampStatusV1.Unavailable, currency, null, null, row.LedgerVersion,
                row.StampVersion.ToString(CultureInfo.InvariantCulture), row.Reason);
        throw new InvalidOperationException();
    }
}
