using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsDbContext
{
    internal const string CustomerReturnStateCheck = "([Status] = 1 AND [Version] = CASE WHEN [WeighingId] IS NULL THEN 1 ELSE 2 END AND [DecidedBy] IS NULL "
                + "AND [DecidedAtUtc] IS NULL AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL) "
                + "OR ([Status] = 2 AND [Version] = CASE WHEN [WeighingId] IS NULL THEN 2 ELSE 3 END AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL "
                + "AND [InventoryMovementId] IS NOT NULL AND [OriginalLabelId] IS NOT NULL AND [OriginalShipmentMovementId] IS NOT NULL) "
                + "OR ([Status] = 3 AND [Version] = CASE WHEN [WeighingId] IS NULL THEN 2 ELSE 3 END AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL "
                + "AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL)";

    internal const string CustomerReturnWeighingCheck = $"([WeighingId] IS NULL AND [WeighingDirection] IS NULL "
                + "AND [FirstWeighingKg] IS NULL AND [SecondWeighingKg] IS NULL AND [NetWeighingKg] IS NULL "
                + "AND [WeighingRecordedBy] IS NULL AND [WeighingRecordedAtUtc] IS NULL) OR "
                + $"([WeighingId] IS NOT NULL AND [WeighingId] <> {EmptyGuid} AND [QuantityUnit] = 2 "
                + "AND [WeighingDirection] IS NOT NULL AND [FirstWeighingKg] IS NOT NULL AND [FirstWeighingKg] > 0 "
                + "AND [SecondWeighingKg] IS NOT NULL AND [SecondWeighingKg] > 0 AND [NetWeighingKg] IS NOT NULL AND [NetWeighingKg] > 0 "
                + "AND [WeighingRecordedBy] IS NOT NULL AND LEN([WeighingRecordedBy]) > 0 AND [WeighingRecordedAtUtc] IS NOT NULL "
                + "AND (([WeighingDirection] = 1 AND [NetWeighingKg] = [SecondWeighingKg] - [FirstWeighingKg]) "
                + "OR ([WeighingDirection] = 2 AND [NetWeighingKg] = [FirstWeighingKg] - [SecondWeighingKg])))";

    private static readonly string[] CustomerReturnWeighingColumns = [nameof(CustomerReturnRow.WeighingId), nameof(CustomerReturnRow.WeighingDirection),
        nameof(CustomerReturnRow.FirstWeighingKg), nameof(CustomerReturnRow.SecondWeighingKg), nameof(CustomerReturnRow.NetWeighingKg),
        nameof(CustomerReturnRow.WeighingRecordedBy), nameof(CustomerReturnRow.WeighingRecordedAtUtc)];
    private static readonly string[] CustomerReturnDecisionColumns = [nameof(CustomerReturnRow.DecidedBy), nameof(CustomerReturnRow.DecidedAtUtc),
        nameof(CustomerReturnRow.DecisionNote), nameof(CustomerReturnRow.InventoryMovementId), nameof(CustomerReturnRow.OriginalLabelId),
        nameof(CustomerReturnRow.OriginalShipmentMovementId)];

    private static void RequireCustomerReturnForwardOnly(EntityEntry<ILogisticsTenantRow> entry)
    {
        var row = (CustomerReturnRow)entry.Entity;
        var status = entry.Property(nameof(CustomerReturnRow.Status)); var version = entry.Property(nameof(CustomerReturnRow.Version));
        bool OriginalNull(string name) => entry.Property(name).OriginalValue is null;
        bool Unchanged(string name) => Equals(entry.Property(name).OriginalValue, entry.Property(name).CurrentValue);
        if ((int)status.OriginalValue! != 1 || !version.IsModified || row.Version != (long)version.OriginalValue! + 1
            || !CustomerReturnDecisionColumns.All(OriginalNull)) throw InvalidReturnTransition();
        if (row.Status == 1)
        {
            // LOJ-D22: only the complete append-once weighing advances Pending1 -> Pending2.
            // All request/source fields are separately protected by the existing modified-column allowlist.
            if (status.IsModified || (long)version.OriginalValue! != 1 || row.Version != 2
                || !CustomerReturnWeighingColumns.All(OriginalNull) || !CustomerReturnDecisionColumns.All(Unchanged)
                || LogisticsCustomerReturnStore.ReadWeighing(row) is null) throw InvalidReturnTransition();
            return;
        }
        // A decision preserves every physical fact and advances the exact unweighed/measured parent version.
        if (!status.IsModified || row.Status is not (2 or 3) || !CustomerReturnWeighingColumns.All(Unchanged)
            || (long)version.OriginalValue! != (entry.Property(nameof(CustomerReturnRow.WeighingId)).OriginalValue is null ? 1L : 2L)
            || string.IsNullOrWhiteSpace(row.DecidedBy) || row.DecidedBy != row.DecidedBy.Trim() || row.DecidedBy == row.RequestedBy
            || row.DecidedAtUtc is not { Offset: var offset } || offset != TimeSpan.Zero
            || row.DecisionNote is not null && (string.IsNullOrWhiteSpace(row.DecisionNote) || row.DecisionNote != row.DecisionNote.Trim()))
            throw InvalidReturnTransition();
        _ = LogisticsCustomerReturnStore.ReadWeighing(row);
        bool Id(Guid? value) => value is { } id && id != Guid.Empty;
        if (row.Status == 2 ? !Id(row.InventoryMovementId) || !Id(row.OriginalLabelId) || !Id(row.OriginalShipmentMovementId)
            : row.InventoryMovementId is not null || row.OriginalLabelId is not null || row.OriginalShipmentMovementId is not null)
            throw InvalidReturnTransition();
    }

    private static InvalidOperationException InvalidReturnTransition() => new("Müşteri iadesi yalnız tek tartım eklemesi veya kanıtı değiştirmeyen tek kararla ilerler.");
}
