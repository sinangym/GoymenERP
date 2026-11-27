using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool PositiveLogisticsPlanVersion(string? value) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var parsed) && parsed > 0 && parsed.ToString(CultureInfo.InvariantCulture) == value;

    private static bool ValidLogisticsPlanExcessProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != LogisticsPlanExcess) return snapshot.LogisticsPlanExcess is null && snapshot.LogisticsPlanExcessProof is null;
        if (snapshot.LogisticsPlanExcess is not { } options || !PositiveLogisticsPlanVersion(options.InstructionVersion)
            || !PositiveLogisticsPlanVersion(snapshot.SourceVersion) || snapshot.LogisticsPlanExcessProof is not { } proof
            || proof.CompanyId != snapshot.CompanyId || proof.ExceptionId != snapshot.Item.ItemId
            || proof.InstructionId == Guid.Empty || proof.LineId == Guid.Empty
            || proof.ProposedBy != snapshot.Item.PreparedBy || string.IsNullOrWhiteSpace(proof.ProposedBy) || proof.ProposedBy.Length > 128
            || proof.ProposedAtUtc == default || proof.ProposedAtUtc != snapshot.Item.WaitingSinceUtc
            || string.IsNullOrWhiteSpace(proof.Reason) || proof.Reason.Trim() != proof.Reason || proof.Reason.Length > 500 || proof.Reason.Any(char.IsControl)
            || !decimal.TryParse(proof.Quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
            || quantity <= 0 || quantity.ToString("0.###", CultureInfo.InvariantCulture) != proof.Quantity)
            return false;
        var instructions = snapshot.Item.References.Where(reference => reference.Role == "loading-instruction").ToArray();
        return instructions.Length == 1 && instructions[0].RecordId == proof.InstructionId;
    }

    private async Task<WorkDecisionResultV1> DecideLogisticsPlanExcessAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (logisticsPlanExcess is null || snapshot.LogisticsPlanExcessProof is not { } proof
            || request.LogisticsPlanExcess is not { } selected) throw new WorkDecisionException(503);
        LogisticsPlanExcessResultV1? answer;
        try
        {
            // Both versions are frozen by the human's request. The current source's instruction version
            // is display context only, never substituted during POST or an old idempotency replay.
            answer = await logisticsPlanExcess.ApproveAsync(id,
                new(request.ExpectedVersion, selected.ExpectedInstructionVersion),
                new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow), token);
        }
        catch (LogisticsInstructionServiceException error)
        { throw new WorkDecisionException(error.Status, error.Code, error.IsReplay, error.OriginalCorrelationId); }
        if (answer?.Exception is not { } value || value.CompanyId != snapshot.CompanyId || value.ExceptionId != id
            || value.InstructionId != proof.InstructionId || value.LineId != proof.LineId || value.Status != "Approved"
            || value.Quantity != proof.Quantity || value.Reason != proof.Reason || value.ProposedBy != proof.ProposedBy
            || value.ProposedAtUtc != proof.ProposedAtUtc || value.ApprovedBy != actor
            || value.ApprovedAtUtc is not { } approvedAt || approvedAt == default || approvedAt.Offset != TimeSpan.Zero
            || !PositiveLogisticsPlanVersion(value.Version)
            || !long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
            || expected == long.MaxValue || long.Parse(value.Version, CultureInfo.InvariantCulture) != expected + 1
            || value.InstructionVersion != selected.ExpectedInstructionVersion
            || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId) || answer.OriginalCorrelationId.Length > 128
            || answer.OriginalCorrelationId.Trim() != answer.OriginalCorrelationId
            || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId)
            throw new WorkDecisionException(503);
        // The native Attempts.ResultJson is immutable. Current LoadedUsed/ShippedUsed/capacity and current
        // instruction version can advance independently and are intentionally not used as receipt proof.
        return new(snapshot.CompanyId, LogisticsPlanExcess, id, "Approved", answer.IsReplay, answer.OriginalCorrelationId);
    }
}
