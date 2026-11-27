using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsDbContext
{
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
