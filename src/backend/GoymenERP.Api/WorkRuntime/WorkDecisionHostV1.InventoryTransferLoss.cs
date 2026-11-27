using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidInventoryTransferLossProof(WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (snapshot.InventoryTransferLoss is not { } options || snapshot.InventoryTransferLossProof is not { } proof
            || proof.TransferId == Guid.Empty || proof.LineId == Guid.Empty || proof.Requests is not { Count: > 0 }
            || options.RequestNumber != proof.Requests.Count
            || proof.Requests.Where((row, index) => row.RequestNumber != index + 1 || row.RequestedAtUtc == default
                || row.RequestedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(row.PreparedBy)
                || row.Status is not ("Pending" or "Approved" or "Rejected" or "Withdrawn")).Any()) return false;
        var current = proof.Requests[^1];
        return current.RequestedAtUtc == options.RequestedAtUtc && current.PreparedBy == snapshot.Item.PreparedBy
            && snapshot.Pending == (current.Status == "Pending")
            && proof.Requests.Take(proof.Requests.Count - 1).All(row => row.Status is "Rejected" or "Withdrawn");
    }

    // Only route a retained, other-maker terminal request to the native key lookup. This is not
    // approval permission or a replay claim: the owner must still prove the exact immutable receipt.
    private static bool MayReachHistoricalLossReceipt(WorkApprovalDecisionSnapshotV1 snapshot, int? selectedNumber, string actor)
    {
        if (!ValidInventoryTransferLossProof(snapshot) || selectedNumber is not > 0
            || snapshot.InventoryTransferLoss is not { } current || selectedNumber >= current.RequestNumber
            || !WorkInboxRulesV1.SameUser(snapshot.Item.PreparedBy, actor)
            || snapshot.Item.ExcludedDeciders?.Any(candidate => WorkInboxRulesV1.SameUser(candidate, actor)) == true)
            return false;
        var selected = snapshot.InventoryTransferLossProof!.Requests.SingleOrDefault(row => row.RequestNumber == selectedNumber);
        return selected is { Status: "Approved" or "Rejected" }
            && !string.IsNullOrWhiteSpace(selected.PreparedBy) && !WorkInboxRulesV1.SameUser(selected.PreparedBy, actor);
    }

    private async Task<WorkDecisionResultV1> DecideInventoryTransferLossAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string? actor, CancellationToken token)
    {
        if (inventoryTransferReceipts is null || snapshot.InventoryTransferLossProof is not { } proof
            || snapshot.InventoryTransferLoss is not { } current || proof.TransferId == Guid.Empty || proof.LineId == Guid.Empty
            || proof.Requests is null || proof.Requests.Count == 0 || proof.Requests.Select(row => row.RequestNumber).Distinct().Count() != proof.Requests.Count)
            throw new WorkDecisionException(503);
        var selection = request.InventoryTransferLoss!;
        if (selection.RequestNumber < 1 || selection.DecidedAtUtc == default || selection.DecidedAtUtc.Offset != TimeSpan.Zero
            || !long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
            || expected < 1 || expected == long.MaxValue || expected.ToString(CultureInfo.InvariantCulture) != request.ExpectedVersion)
            throw new WorkDecisionException(400, "work_decision_invalid");
        var selected = proof.Requests.SingleOrDefault(row => row.RequestNumber == selection.RequestNumber);
        if (selected is null) throw new WorkDecisionException(409, "work_decision_source_changed");
        if (string.IsNullOrWhiteSpace(selected.PreparedBy) || selected.RequestedAtUtc == default
            || selected.RequestedAtUtc.Offset != TimeSpan.Zero || selection.DecidedAtUtc < selected.RequestedAtUtc)
            throw new WorkDecisionException(400, "work_decision_invalid");
        if (WorkInboxRulesV1.SameUser(selected.PreparedBy, actor)) throw new WorkDecisionException(403);
        try
        {
            var answer = await inventoryTransferReceipts.DecideLossAsync(proof.TransferId, proof.LineId,
                new(request.ExpectedVersion, request.Approve ? InventoryTransferDecisionKindV1.Approve : InventoryTransferDecisionKindV1.Reject,
                    selection.DecidedAtUtc, request.Reason!, selection.RequestNumber),
                new(actor!, InventoryPermissionManifestV1.StockTransferLossApprove, [], correlation.CorrelationId,
                    correlation.CausationId, key, "work"), token);
            // Old numbered requests can only complete via the original immutable native key receipt.
            // Never substitute the latest request, its maker, or its timestamp for the captured payload.
            if (answer is null || !answer.CommandReceiptProven) throw new WorkDecisionException(503);
            if (!answer.IsReplay && (selection.RequestNumber != current.RequestNumber || !snapshot.Pending))
                throw new WorkDecisionException(409, "work_decision_source_changed");
            var transfer = answer.Transfer; var facts = answer.Facts;
            var rows = facts?.Losses?.Where(row => row.LineId == proof.LineId && row.RequestNumber == selection.RequestNumber).ToArray();
            if (transfer is null || facts is null || transfer.CompanyId != snapshot.CompanyId || facts.CompanyId != snapshot.CompanyId
                || transfer.TransferId != proof.TransferId || facts.TransferId != proof.TransferId || transfer.Version != facts.Version
                || transfer.Status != facts.Status || transfer.Lines is null || transfer.Lines.Count(row => row.LineId == proof.LineId) != 1
                || transfer.Lines.Single(row => row.LineId == proof.LineId).Status != (request.Approve ? "WrittenOff" : "InTransit")
                || !long.TryParse(transfer.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var actual)
                || actual != expected + (request.Approve ? 1 : 0) || actual.ToString(CultureInfo.InvariantCulture) != transfer.Version
                || rows is not { Length: 1 } || rows[0].Status != (request.Approve ? "Approved" : "Rejected")
                || selected.Status != "Pending" && selected.Status != rows[0].Status
                || rows[0].RequestedAtUtc != selected.RequestedAtUtc || rows[0].RequestedBy != selected.PreparedBy
                || rows[0].DecidedBy != actor || rows[0].DecidedAtUtc != selection.DecidedAtUtc
                || rows[0].DecisionReason != request.Reason?.Trim()
                || (request.Approve ? rows[0].MovementId is null || rows[0].MovementId == Guid.Empty : rows[0].MovementId is not null)
                || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId))
                throw new WorkDecisionException(503);
            return new(snapshot.CompanyId, InventoryTransferLoss, id, rows[0].Status, answer.IsReplay, answer.OriginalCorrelationId);
        }
        catch (InventoryTransferException error) { throw new WorkDecisionException(error.Status, error.Code); }
    }
}
