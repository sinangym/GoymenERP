using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool PositiveWeighingVersion(string? value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0 && version.ToString(CultureInfo.InvariantCulture) == value;

    private static bool ValidLogisticsWeighingProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != LogisticsWeighingDifference) return snapshot.LogisticsWeighingDifferenceProof is null;
        if (snapshot.LogisticsWeighingDifferenceProof is not { } proof || proof.CompanyId != snapshot.CompanyId
            || proof.RecordId != snapshot.Item.ItemId || proof.DispatchNoteId == Guid.Empty || proof.InstructionId == Guid.Empty
            || !PositiveWeighingVersion(snapshot.SourceVersion) || !proof.EnteredByComplete
            || proof.RecordedBy != snapshot.Item.PreparedBy || string.IsNullOrWhiteSpace(proof.RecordedBy)
            || proof.RecordedAtUtc == default || proof.RecordedAtUtc.Offset != TimeSpan.Zero || proof.RecordedAtUtc != snapshot.Item.WaitingSinceUtc
            || proof.EnteredBy is not { Count: > 0 } || proof.EnteredBy.Count > WorkInboxRulesV1.MaxExcludedDecidersPerItem + 1
            || proof.EnteredBy.Any(actor => string.IsNullOrWhiteSpace(actor) || actor != actor.Trim() || actor.Length > WeighingDifferenceRecord.MaximumActorLength)
            || proof.EnteredBy.Distinct(StringComparer.Ordinal).Count() != proof.EnteredBy.Count
            || !proof.EnteredBy.Contains(proof.RecordedBy, StringComparer.Ordinal)
            || proof.Products is not { Count: > 0 } || proof.Products.Count > WeighingDifferenceCalculator.MaximumLines
            || proof.Products.Any(product => product.ProductId == Guid.Empty || product.NoteKg < 0 || product.ScaleKg < 0
                || product.RemainingPhysicalKg < 0 || product.PhysicallyCoveredKg < 0 || product.ExcessKg < 0)
            || proof.Products.Select(product => product.ProductId).Distinct().Count() != proof.Products.Count
            || proof.TheoreticalKg < 0 || proof.ScaleKg < 0 || proof.TotalExcessKg < 0
            || !Enum.TryParse<WeighingDifferenceBandLevel>(proof.BandLevel, out var band) || !Enum.IsDefined(band) || band.ToString() != proof.BandLevel
            || !Enum.TryParse<WeighingDifferenceDecision>(proof.Decision, out var decision) || !Enum.IsDefined(decision) || decision.ToString() != proof.Decision)
            return false;
        var excluded = proof.EnteredBy.Where(actor => !WorkInboxRulesV1.SameUser(actor, proof.RecordedBy)).Order(StringComparer.Ordinal).ToArray();
        return excluded.SequenceEqual((snapshot.Item.ExcludedDeciders ?? []).Order(StringComparer.Ordinal), StringComparer.Ordinal)
            && snapshot.Item.References.Count(reference => reference.Role == "dispatch-note" && reference.RecordId == proof.DispatchNoteId) == 1
            && snapshot.Item.References.Count(reference => reference.Role == "loading-instruction" && reference.RecordId == proof.InstructionId) == 1;
    }

    private async Task<WorkDecisionResultV1> DecideLogisticsWeighingDifferenceAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (logisticsWeighingDifferences is null || snapshot.LogisticsWeighingDifferenceProof is not { } proof)
            throw new WorkDecisionException(503);
        var expected = int.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        if (expected == int.MaxValue) throw new WorkDecisionException(400, "work_decision_invalid");
        WeighingDifferenceResponseV1? answer;
        try
        {
            answer = await logisticsWeighingDifferences.ApproveAsync(id, new(expected),
                new(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow), token);
        }
        catch (LogisticsWeighingDifferenceServiceException error) { throw new WorkDecisionException(error.Status, error.Code); }
        if (answer?.Record is not { } value || !answer.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)
            || value.CompanyId != snapshot.CompanyId || value.RecordId != id
            || value.DispatchNoteId != proof.DispatchNoteId || value.InstructionId != proof.InstructionId
            || value.Status != "Approved" || value.Decision != "ApprovalRequired" || value.Version != expected + 1 || value.Decision != proof.Decision
            || value.TheoreticalKg != proof.TheoreticalKg || value.ScaleKg != proof.ScaleKg || value.DifferenceKg != proof.DifferenceKg
            || value.BandLevel != proof.BandLevel || value.WarningBandPercent != proof.WarningBandPercent
            || value.ApprovalBandPercent != proof.ApprovalBandPercent || value.BlockBandPercent != proof.BlockBandPercent
            || value.TotalExcessKg != proof.TotalExcessKg || value.ApprovalRatePercent != proof.ApprovalRatePercent
            || value.ExcessRatioPercent != WeighingDifferenceCalculator.ExcessRatioPercent(proof.TotalExcessKg, proof.TheoreticalKg)
            || value.RecordedBy != proof.RecordedBy || value.RecordedAtUtc != proof.RecordedAtUtc || !value.EnteredByComplete
            || value.EnteredBy is null || !value.EnteredBy.Order(StringComparer.Ordinal).SequenceEqual(proof.EnteredBy.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || value.ApprovedBy != actor || value.ApprovedAtUtc is not { } approvedAt || approvedAt == default || approvedAt.Offset != TimeSpan.Zero
            || value.Products is null || !value.Products.Select(product => new WorkLogisticsWeighingDifferenceProductProofV1(product.ProductId,
                product.NoteKg, product.ScaleKg, product.DifferenceKg, product.RemainingPhysicalKg, product.PhysicallyCoveredKg,
                product.ExcessKg, product.TargetLineNo)).SequenceEqual(proof.Products)
            || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId) || answer.OriginalCorrelationId.Trim() != answer.OriginalCorrelationId
            || answer.OriginalCorrelationId.Length > 128 || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId)
            throw new WorkDecisionException(503);
        try
        {
            var actual = value.KilogramAllocation is null ? null : JsonSerializer.SerializeToNode(value.KilogramAllocation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var stored = proof.KilogramAllocationJson is null ? null : JsonNode.Parse(proof.KilogramAllocationJson);
            if (!JsonNode.DeepEquals(actual, stored)) throw new WorkDecisionException(503);
        }
        catch (JsonException) { throw new WorkDecisionException(503); }
        // Only the native immutable result is receipt proof. No current note/shipment/scale/stock recheck after commit.
        return new(snapshot.CompanyId, LogisticsWeighingDifference, id, value.Status, answer.IsReplay, answer.OriginalCorrelationId);
    }
}
