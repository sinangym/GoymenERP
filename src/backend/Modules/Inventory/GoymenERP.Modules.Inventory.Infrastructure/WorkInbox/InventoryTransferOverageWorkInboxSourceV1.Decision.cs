using System.Globalization;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

internal sealed partial class InventoryTransferOverageWorkInboxSourceV1
{
    // Hash identities cannot be reversed. Overflow is explicitly unavailable, never a truncated match/history.
    internal const int DecisionReadLimit = 1000;

    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context,
        Guid itemId, CancellationToken token)
    {
        async Task Fence()
        {
            if (await fence.CheckAsync(context, ApproverPermission, token) is not null)
                throw new InvalidOperationException("Overage decision source unavailable.");
        }
        await Fence();
        var company = context.CompanyId;
        var locators = await rows.ListAsync(rows.TransferReceipts.Where(row => row.CompanyId == company && row.OverageKg > 0)
            .Select(row => new { row.TransferId, row.LineId }).Distinct().OrderBy(row => row.TransferId).ThenBy(row => row.LineId)
            .Take(DecisionReadLimit + 1), token);
        await Fence();
        if (locators.Count > DecisionReadLimit) throw new InvalidOperationException("Overage locator bound exceeded.");
        var matches = locators.Where(row => InventoryWorkInboxFence.TransferLineItemId(KindName, row.TransferId, row.LineId) == itemId).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1) throw new InvalidOperationException("Ambiguous overage identity.");
        var transferId = matches[0].TransferId; var lineId = matches[0].LineId;
        var first = await rows.ListAsync(
            (from receipt in rows.TransferReceipts
             join transfer in rows.Transfers on new { receipt.CompanyId, Id = receipt.TransferId } equals new { transfer.CompanyId, transfer.Id }
             join line in rows.TransferLines on new { receipt.CompanyId, receipt.TransferId, receipt.LineId } equals new { line.CompanyId, line.TransferId, line.LineId }
             join label in rows.Labels on new { line.CompanyId, Id = line.LabelId } equals new { label.CompanyId, label.Id }
             where receipt.CompanyId == company && receipt.TransferId == transferId && receipt.LineId == lineId
             select new { transfer.Version, transfer.DestinationLocationId, line.LabelId, LabelCode = label.Code,
                 receipt.DispatchedKg, receipt.ReceivedKg, receipt.OverageKg, receipt.DispatchedPieceCount,
                 receipt.ReceivedPieceCount, receipt.OveragePieceCount, receipt.RecordedBy, receipt.RecordedAtUtc,
                 receipt.ReceivedAtUtc, receipt.OverageReason }).Take(2), token);
        await Fence();
        if (first.Count != 1) throw new InvalidOperationException("Missing/ambiguous overage receipt.");
        var receiptRow = first[0];
        var renewals = await rows.ListAsync(rows.TransferOverageRenewals.Where(row => row.CompanyId == company
            && row.TransferId == transferId && row.LineId == lineId).OrderBy(row => row.RequestNumber).Take(DecisionReadLimit + 1), token);
        await Fence();
        var decisions = await rows.ListAsync(rows.TransferOverageDecisions.Where(row => row.CompanyId == company
            && row.TransferId == transferId && row.LineId == lineId).OrderBy(row => row.RequestNumber).Take(DecisionReadLimit + 1), token);
        await Fence();
        if (renewals.Count >= DecisionReadLimit || decisions.Count > DecisionReadLimit || receiptRow.Version < 1
            || renewals.Where((row, index) => row.RequestNumber != index + 2).Any()
            || decisions.Select(row => row.RequestNumber).Distinct().Count() != decisions.Count
            || decisions.Any(row => row.RequestNumber < 1 || row.RequestNumber > renewals.Count + 1 || row.Decision is not (1 or 2)))
            throw new InvalidOperationException("Incomplete native overage history.");
        string Status(int number) => decisions.SingleOrDefault(row => row.RequestNumber == number)?.Decision switch
            { null => "Pending", 1 => "Approved", 2 => "Rejected", _ => throw new InvalidOperationException() };
        var requests = new List<WorkInventoryTransferOverageRequestProofV1>
        {
            new(1, receiptRow.ReceivedAtUtc, receiptRow.RecordedBy.Trim(), Status(1),
                WorkInboxRulesV1.Decimal(receiptRow.OverageKg), receiptRow.OveragePieceCount, receiptRow.OverageReason ?? "")
        };
        requests.AddRange(renewals.Select(row => new WorkInventoryTransferOverageRequestProofV1(row.RequestNumber,
            row.RequestedAtUtc, row.RecordedBy.Trim(), Status(row.RequestNumber), WorkInboxRulesV1.Decimal(row.OverageKg), row.OveragePieceCount, row.Reason)));
        if (requests.Any(row => row.RequestedAtUtc == default || row.RequestedAtUtc.Offset != TimeSpan.Zero
                || string.IsNullOrWhiteSpace(row.PreparedBy) || string.IsNullOrWhiteSpace(row.Reason) || row.Reason.Trim() != row.Reason
                || row.Reason.Length > 500 || row.Reason.Any(char.IsControl)
                || !decimal.TryParse(row.Kilograms, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kg)
                || kg <= 0 || kg > receiptRow.OverageKg || row.PieceCount is < 0)
            || requests.Take(requests.Count - 1).Any(row => row.Status != "Rejected"))
            throw new InvalidOperationException("Malformed native overage request.");
        var current = requests[^1]; var lastRenewal = renewals.LastOrDefault();
        if (!TryItem(new(transferId, lineId, receiptRow.DestinationLocationId, receiptRow.LabelId, receiptRow.LabelCode,
            current.RequestNumber, lastRenewal?.RecordedAtUtc ?? receiptRow.RecordedAtUtc, current.PreparedBy, receiptRow.RecordedBy,
            current.RequestNumber == 1 ? receiptRow.DispatchedKg : null, current.RequestNumber == 1 ? receiptRow.ReceivedKg : null,
            receiptRow.OverageKg, lastRenewal?.OverageKg, current.RequestNumber == 1 ? receiptRow.DispatchedPieceCount : null,
            current.RequestNumber == 1 ? receiptRow.ReceivedPieceCount : null, (current.PieceCount == 0 ? null : current.PieceCount)), out var item))
            throw new InvalidOperationException("Malformed overage purpose item.");
        return new(company, item, receiptRow.Version.ToString(CultureInfo.InvariantCulture), current.Status == "Pending",
            InventoryTransferOverageProof: new(transferId, lineId, receiptRow.RecordedBy.Trim(), requests));
    }
}
