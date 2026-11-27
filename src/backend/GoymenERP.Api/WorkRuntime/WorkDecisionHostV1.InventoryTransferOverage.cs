using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidInventoryTransferOverageProof(WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (snapshot.InventoryTransferOverageProof is not { } proof || proof.TransferId == Guid.Empty || proof.LineId == Guid.Empty
            || string.IsNullOrWhiteSpace(proof.ReceivedBy) || proof.Requests is not { Count: > 0 }
            || proof.Requests.Where((row, index) => row.RequestNumber != index + 1 || row.RequestedAtUtc == default
                || row.RequestedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(row.PreparedBy)
                || row.Status is not ("Pending" or "Approved" or "Rejected") || row.PieceCount is < 0
                || string.IsNullOrWhiteSpace(row.Reason) || InventoryTransferReceiptApiV1.NormalizeText(row.Reason) != row.Reason
                || !decimal.TryParse(row.Kilograms, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kg)
                || kg <= 0 || kg.ToString("0.###", CultureInfo.InvariantCulture) != row.Kilograms).Any()
            || proof.Requests.Take(proof.Requests.Count - 1).Any(row => row.Status != "Rejected")) return false;
        var current = proof.Requests[^1];
        var otherExcluded = snapshot.Item.ExcludedDeciders ?? [];
        var expectedOther = !WorkInboxRulesV1.SameUser(proof.ReceivedBy, current.PreparedBy);
        return current.PreparedBy == snapshot.Item.PreparedBy && proof.Requests[0].PreparedBy == proof.ReceivedBy
            && snapshot.Pending == (current.Status == "Pending")
            && otherExcluded.Count == (expectedOther ? 1 : 0)
            && (!expectedOther || WorkInboxRulesV1.SameUser(otherExcluded[0], proof.ReceivedBy))
            && snapshot.Item.References.Count(reference => reference.Role == "inventory-transfer" && reference.RecordId == proof.TransferId) == 1;
    }

    private static bool MayReachHistoricalOverageReceipt(WorkApprovalDecisionSnapshotV1 snapshot, int? number, string actor)
    {
        if (!ValidInventoryTransferOverageProof(snapshot) || number is not > 0
            || snapshot.InventoryTransferOverageProof is not { } proof || number >= proof.Requests.Count
            || !WorkInboxRulesV1.SameUser(snapshot.Item.PreparedBy, actor)
            || snapshot.Item.ExcludedDeciders?.Any(other => WorkInboxRulesV1.SameUser(other, actor)) == true)
            return false;
        var selected = proof.Requests.Single(row => row.RequestNumber == number);
        return selected.Status is "Approved" or "Rejected" && !WorkInboxRulesV1.SameUser(selected.PreparedBy, actor)
            && !WorkInboxRulesV1.SameUser(proof.ReceivedBy, actor);
    }

    private static WorkInventoryTransferOveragePreviewV1 OveragePreview(WorkApprovalDecisionSnapshotV1 snapshot)
    {
        var proof = snapshot.InventoryTransferOverageProof!; var current = proof.Requests[^1];
        return new(current.RequestNumber, current.RequestedAtUtc, proof.TransferId, proof.LineId, current.Kilograms,
            current.PieceCount, current.Reason, current.PreparedBy, proof.ReceivedBy);
    }

    private async Task<WorkDecisionResultV1> DecideInventoryTransferOverageAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (inventoryTransferReceipts is null || snapshot.InventoryTransferOverageProof is not { } proof)
            throw new WorkDecisionException(503);
        var selection = request.InventoryTransferOverage!;
        if (selection.RequestNumber < 1 || selection.DecidedAtUtc == default || selection.DecidedAtUtc.Offset != TimeSpan.Zero
            || !PositiveLogisticsPlanVersion(request.ExpectedVersion)) throw new WorkDecisionException(400, "work_decision_invalid");
        var selected = proof.Requests.SingleOrDefault(row => row.RequestNumber == selection.RequestNumber);
        if (selected is null) throw new WorkDecisionException(409, "work_decision_source_changed");
        if (selection.DecidedAtUtc < selected.RequestedAtUtc) throw new WorkDecisionException(400, "work_decision_invalid");
        if (WorkInboxRulesV1.SameUser(selected.PreparedBy, actor) || WorkInboxRulesV1.SameUser(proof.ReceivedBy, actor))
            throw new WorkDecisionException(403);
        try
        {
            var answer = await inventoryTransferReceipts.DecideOverageAsync(proof.TransferId, proof.LineId,
                new(request.ExpectedVersion, request.Approve ? InventoryTransferDecisionKindV1.Approve : InventoryTransferDecisionKindV1.Reject,
                    selection.DecidedAtUtc, request.Reason!, selection.RequestNumber),
                new(actor, InventoryPermissionManifestV1.StockTransferOverageApprove, [], correlation.CorrelationId,
                    correlation.CausationId, key, "work"), token);
            if (answer is null || !answer.CommandReceiptProven) throw new WorkDecisionException(503);
            if (!answer.IsReplay && (selection.RequestNumber != proof.Requests.Count || !snapshot.Pending))
                throw new WorkDecisionException(409, "work_decision_source_changed");
            var transfer = answer.Transfer; var facts = answer.Facts;
            if (transfer is null || facts is null || transfer.CompanyId != snapshot.CompanyId || facts.CompanyId != snapshot.CompanyId
                || transfer.TransferId != proof.TransferId || facts.TransferId != proof.TransferId
                || transfer.Version != request.ExpectedVersion || facts.Version != request.ExpectedVersion || transfer.Status != facts.Status
                || transfer.Lines?.Count(row => row.LineId == proof.LineId) != 1
                || facts.Receipts?.Count(row => row.LineId == proof.LineId) != 1
                || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)) throw new WorkDecisionException(503);
            var receipt = facts.Receipts.Single(row => row.LineId == proof.LineId);
            var renewals = facts.OverageRenewals?.Where(row => row.LineId == proof.LineId && row.RequestNumber == selection.RequestNumber).ToArray();
            if (selection.RequestNumber > 1 && renewals is not { Length: 1 }) throw new WorkDecisionException(503);
            var renewal = selection.RequestNumber == 1 ? null : renewals![0];
            var status = renewal?.Status ?? receipt.OverageStatus;
            var maker = renewal?.RequestedBy ?? receipt.ReceivedBy;
            var at = renewal?.RequestedAtUtc ?? receipt.ReceivedAtUtc;
            var kg = renewal?.OverageKg ?? receipt.OverageKg;
            var reason = renewal?.Reason ?? receipt.OverageReason;
            var pieces = renewal is null ? receipt.OveragePieceCount : renewal.OveragePieceCount;
            var movement = renewal is null ? receipt.OverageMovementId : renewal.MovementId;
            var decidedBy = renewal is null ? receipt.OverageDecidedBy : renewal.DecidedBy;
            var decidedAt = renewal is null ? receipt.OverageDecidedAtUtc : renewal.DecidedAtUtc;
            var decisionReason = renewal is null ? receipt.OverageDecisionReason : renewal.DecisionReason;
            if (receipt.ReceivedBy != proof.ReceivedBy || maker != selected.PreparedBy || at != selected.RequestedAtUtc
                || kg != selected.Kilograms || pieces != selected.PieceCount || reason != selected.Reason
                || status != (request.Approve ? "Approved" : "Rejected") || selected.Status != "Pending" && selected.Status != status
                || decidedBy != actor || decidedAt != selection.DecidedAtUtc || decisionReason != request.Reason?.Trim()
                || (request.Approve ? movement is null || movement == Guid.Empty : movement is not null))
                throw new WorkDecisionException(503);
            // Validate the immutable native key result, never substitute current transfer/label/usage state after a later command.
            return new(snapshot.CompanyId, InventoryTransferOverage, id, status, answer.IsReplay, answer.OriginalCorrelationId);
        }
        catch (InventoryTransferException error) { throw new WorkDecisionException(error.Status, error.Code); }
    }
}
