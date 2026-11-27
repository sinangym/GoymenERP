using System.Globalization;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;

internal sealed partial class InventoryTransferLossWorkInboxSourceV1
{
    // The public item is a hash of kind/transfer/line, not an invertible owner ID. Bound both
    // locator discovery and retained request history; overflow is unavailable, never a partial proof.
    internal const int DecisionReadLimit = 1000;

    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context,
        Guid itemId, CancellationToken cancellationToken)
    {
        async Task RequireFence()
        {
            if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is not null)
                throw new InvalidOperationException("The loss decision source is unavailable.");
        }
        await RequireFence();
        var company = context.CompanyId;
        var locators = await rows.ListAsync(rows.TransferLossRequests.Where(request => request.CompanyId == company)
            .Select(request => new { request.TransferId, request.LineId }).Distinct()
            .OrderBy(row => row.TransferId).ThenBy(row => row.LineId).Take(DecisionReadLimit + 1), cancellationToken);
        await RequireFence();
        if (locators.Count > DecisionReadLimit) throw new InvalidOperationException("Loss locator bound exceeded.");
        var matches = locators.Where(row => InventoryWorkInboxFence.TransferLineItemId(KindName, row.TransferId, row.LineId) == itemId).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1) throw new InvalidOperationException("Ambiguous loss identity.");
        var transferId = matches[0].TransferId; var lineId = matches[0].LineId;
        var history = await rows.ListAsync(
            (from request in rows.TransferLossRequests
             join transfer in rows.Transfers on new { request.CompanyId, Id = request.TransferId } equals new { transfer.CompanyId, transfer.Id }
             join line in rows.TransferLines on new { request.CompanyId, request.TransferId, request.LineId } equals new { line.CompanyId, line.TransferId, line.LineId }
             join label in rows.Labels on new { line.CompanyId, Id = line.LabelId } equals new { label.CompanyId, label.Id }
             where request.CompanyId == company && request.TransferId == transferId && request.LineId == lineId
             orderby request.RequestNumber
             select new
             {
                 request.RequestNumber, request.RequestedAtUtc, request.RecordedAtUtc, request.RecordedBy,
                 transfer.Version, transfer.DestinationLocationId, line.LabelId, LabelCode = label.Code,
                 DispatchedKg = rows.TransferDispatchLines.Where(dispatch => dispatch.CompanyId == company
                     && dispatch.TransferId == transferId && dispatch.LineId == lineId).Select(dispatch => (decimal?)dispatch.ActualKg).FirstOrDefault(),
                 Decision = rows.TransferLossDecisions.Where(decision => decision.CompanyId == company && decision.TransferId == transferId
                     && decision.LineId == lineId && decision.RequestNumber == request.RequestNumber).Select(decision => (int?)decision.Decision).FirstOrDefault(),
                 Withdrawn = rows.TransferLossWithdrawals.Any(withdrawal => withdrawal.CompanyId == company && withdrawal.TransferId == transferId
                     && withdrawal.LineId == lineId && withdrawal.RequestNumber == request.RequestNumber),
             }).Take(DecisionReadLimit + 1), cancellationToken);
        await RequireFence();
        if (history.Count == 0 || history.Count > DecisionReadLimit
            || history.Where((row, index) => row.RequestNumber != index + 1).Any()
            || history.Any(row => row.Version < 1 || row.RequestedAtUtc == default || row.RequestedAtUtc.Offset != TimeSpan.Zero
                || row.RecordedAtUtc == default || string.IsNullOrWhiteSpace(row.RecordedBy)
                || row.Decision is not (null or 1 or 2) || row.Decision is not null && row.Withdrawn)
            || history.Take(history.Count - 1).Any(row => row.Decision != 2 && !row.Withdrawn))
            throw new InvalidOperationException("Incomplete native loss history.");
        var current = history[^1];
        if (!TryItem(new(transferId, lineId, current.RequestNumber, current.DestinationLocationId, current.LabelId,
            current.LabelCode, current.DispatchedKg, current.RecordedBy, current.RecordedAtUtc), out var item))
            throw new InvalidOperationException("Malformed native loss source.");
        var requests = history.Select(row => new WorkInventoryTransferLossRequestProofV1(row.RequestNumber,
            row.RequestedAtUtc, row.RecordedBy.Trim(), row.Withdrawn ? "Withdrawn" : row.Decision == 1 ? "Approved" : row.Decision == 2 ? "Rejected" : "Pending")).ToArray();
        return new(company, item, current.Version.ToString(CultureInfo.InvariantCulture), current.Decision is null && !current.Withdrawn,
            InventoryTransferLoss: new(current.RequestNumber, current.RequestedAtUtc),
            InventoryTransferLossProof: new(transferId, lineId, requests));
    }
}
