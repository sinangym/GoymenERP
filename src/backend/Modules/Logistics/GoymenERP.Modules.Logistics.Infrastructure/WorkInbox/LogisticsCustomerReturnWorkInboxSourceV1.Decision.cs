using System.Globalization;
using GoymenERP.BuildingBlocks.Quantities;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Domain.Returns;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;

namespace GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;

internal sealed partial class LogisticsCustomerReturnWorkInboxSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken token)
    {
        async Task Fence()
        {
            if (await fence.CheckAsync(context, ApproverPermission, token) is not null)
                throw new InvalidOperationException("Customer return purpose unavailable.");
        }
        await Fence();
        if (itemId == Guid.Empty) return null;
        var query = rows.CustomerReturns.Where(row => row.CompanyId == context.CompanyId && row.CustomerReturnId == itemId);
        var found = await rows.ListAsync(query.Take(2), token); await Fence();
        if (found.Count == 0) return null;
        if (found.Count != 1 || !ValidReturn(found[0])) throw new InvalidOperationException("Incomplete native return.");
        var row = found[0]; var human = Human(row); var version = row.Version;
        var pending = row.Status == (int)CustomerReturnStatus.Pending;
        if (!TryItem(new(row.CustomerReturnId, row.LoadingInstructionId, row.QuantityUnit, row.Quantity,
                row.SourceDeliveredQuantity, row.RequestedBy, row.RequestedAtUtc), out var item))
            throw new InvalidOperationException("Incomplete native return caption.");
        // The owner model forbids changing request fields. Still compare the complete released
        // projection and decision version before release; an incomplete/changing read is never ready.
        var again = await rows.ListAsync(query.Take(2), token); await Fence();
        if (again.Count != 1 || !ValidReturn(again[0]) || again[0].Version != version || Human(again[0]) != human)
            throw new InvalidOperationException("Customer return changed during purpose read.");
        return new(context.CompanyId, item, version.ToString(CultureInfo.InvariantCulture), pending,
            LogisticsCustomerReturnProof: new(context.CompanyId, itemId, human));
    }

    private static bool ValidReturn(CustomerReturnRow row)
    {
        var weighing = LogisticsCustomerReturnStore.ReadWeighing(row);
        bool Ids() => row.CompanyId != Guid.Empty && row.CustomerReturnId != Guid.Empty && row.LoadingInstructionId != Guid.Empty
            && row.LoadingInstructionLineId != Guid.Empty && row.SourceDeliveryId != Guid.Empty && row.SourceShipmentId != Guid.Empty
            && row.SourceOrderId != Guid.Empty && row.SourceOrderLineId != Guid.Empty && row.UnitId != Guid.Empty;
        bool Utc(DateTimeOffset value) => value != default && value.Offset == TimeSpan.Zero;
        if (!Ids() || row.QuantityUnit != (int)QuantityUnit.Kilogram && row.QuantityUnit != (int)QuantityUnit.Piece
            || row.Quantity <= 0 || row.Quantity > row.SourceDeliveredQuantity
            || row.QuantityUnit == (int)QuantityUnit.Piece && (decimal.Truncate(row.Quantity) != row.Quantity || decimal.Truncate(row.SourceDeliveredQuantity) != row.SourceDeliveredQuantity)
            || string.IsNullOrWhiteSpace(row.Reason) || row.Reason != row.Reason.Trim()
            || string.IsNullOrWhiteSpace(row.RequestedBy) || row.RequestedBy != row.RequestedBy.Trim() || row.RequestedBy.Length > 128
            || row.OccurredAtUtc.Offset != TimeSpan.Zero || !Utc(row.RequestedAtUtc)) return false;
        if (row.Status == (int)CustomerReturnStatus.Pending)
            return row.Version == (weighing is null ? 1 : 2) && row.DecidedBy is null && row.DecidedAtUtc is null && row.DecisionNote is null
                && row.InventoryMovementId is null && row.OriginalLabelId is null && row.OriginalShipmentMovementId is null;
        if (row.Version != (weighing is null ? 2 : 3) || string.IsNullOrWhiteSpace(row.DecidedBy) || row.DecidedBy.Length > 128
            || row.DecidedBy.Trim() != row.DecidedBy || row.DecidedBy == row.RequestedBy || row.DecidedAtUtc is not { } at || !Utc(at)
            || row.DecisionNote is not null && (string.IsNullOrWhiteSpace(row.DecisionNote) || row.DecisionNote.Trim() != row.DecisionNote)) return false;
        return row.Status switch
        {
            (int)CustomerReturnStatus.Approved => row.InventoryMovementId is { } move && move != Guid.Empty
                && row.OriginalLabelId is { } label && label != Guid.Empty && row.OriginalShipmentMovementId is { } original && original != Guid.Empty,
            (int)CustomerReturnStatus.Rejected => row.InventoryMovementId is null && row.OriginalLabelId is null && row.OriginalShipmentMovementId is null,
            _ => false,
        };
    }

    private static WorkLogisticsCustomerReturnPreviewV1 Human(CustomerReturnRow row) => new(row.LoadingInstructionId,
        row.LoadingInstructionLineId, row.SourceDeliveryId, row.SourceShipmentId, row.SourceOrderId, row.SourceOrderLineId, row.UnitId,
        ((QuantityUnit)row.QuantityUnit).ToString(), row.Quantity.ToString(CultureInfo.InvariantCulture), row.SourceDeliveredQuantity.ToString(CultureInfo.InvariantCulture),
        row.Reason, row.RequestedBy, row.OccurredAtUtc, row.RequestedAtUtc, ((CustomerReturnStatus)row.Status).ToString(), HumanWeighing(row));

    private static WorkCustomerReturnWeighingV1? HumanWeighing(CustomerReturnRow row) => LogisticsCustomerReturnStore.ReadWeighing(row) is { } value
        ? new(value.WeighingId, value.Direction, value.FirstWeighingKg, value.SecondWeighingKg, value.NetKg, value.RecordedBy, value.RecordedAtUtc) : null;
}
