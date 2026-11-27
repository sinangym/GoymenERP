using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool CustomerReturnVersion(string? text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture,
        out var value) && value > 0 && value < long.MaxValue && value.ToString(CultureInfo.InvariantCulture) == text;

    private static bool ValidLogisticsCustomerReturnRequest(WorkDecisionRequestV1 request) => CustomerReturnVersion(request.ExpectedVersion)
        && (request.Approve ? request.LogisticsCustomerReturn is { InventoryMovementId: var movement } && movement != Guid.Empty
            : request.LogisticsCustomerReturn is null);

    private static bool ValidLogisticsCustomerReturnProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != LogisticsCustomerReturn) return snapshot.LogisticsCustomerReturnProof is null;
        if (snapshot.LogisticsCustomerReturnProof is not { Return: { } facts } proof || proof.CompanyId != snapshot.CompanyId
            || proof.CustomerReturnId != snapshot.Item.ItemId || proof.CustomerReturnId == Guid.Empty
            || facts.LoadingInstructionId == Guid.Empty || facts.LoadingInstructionLineId == Guid.Empty
            || facts.SourceDeliveryId == Guid.Empty || facts.SourceShipmentId == Guid.Empty || facts.SourceOrderId == Guid.Empty
            || facts.SourceOrderLineId == Guid.Empty || facts.UnitId == Guid.Empty || facts.QuantityUnit is not ("Kilogram" or "Piece")
            || !decimal.TryParse(facts.Quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
            || !decimal.TryParse(facts.SourceDeliveredQuantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var delivered)
            || quantity <= 0 || quantity > delivered || quantity.ToString(CultureInfo.InvariantCulture) != facts.Quantity
            || delivered.ToString(CultureInfo.InvariantCulture) != facts.SourceDeliveredQuantity
            || facts.QuantityUnit == "Piece" && (decimal.Truncate(quantity) != quantity || decimal.Truncate(delivered) != delivered)
            || string.IsNullOrWhiteSpace(facts.Reason) || facts.Reason.Trim() != facts.Reason
            || string.IsNullOrWhiteSpace(facts.RequestedBy) || facts.RequestedBy.Trim() != facts.RequestedBy || facts.RequestedBy.Length > 128
            || facts.RequestedBy != snapshot.Item.PreparedBy || facts.RequestedAtUtc != snapshot.Item.WaitingSinceUtc
            || facts.RequestedAtUtc == default || facts.RequestedAtUtc.Offset != TimeSpan.Zero
            || facts.OccurredAtUtc.Offset != TimeSpan.Zero
            || !ValidReturnWeighing(facts) || snapshot.Pending != (facts.Status == "Pending")
            || snapshot.SourceVersion != (snapshot.Pending ? facts.Weighing is null ? "1" : "2" : facts.Weighing is null ? "2" : "3")
            || facts.Status is not ("Pending" or "Approved" or "Rejected") || snapshot.Item.ExcludedDeciders is { Count: > 0 }
            || snapshot.Item.References.Count != 1 || snapshot.Item.References[0].Role != "loading-instruction"
            || snapshot.Item.References[0].RecordId != facts.LoadingInstructionId
            || snapshot.Item.Rules.Count != 1 || snapshot.Item.Rules[0] is not { } rule
            || rule.RuleCode != "logistics.customer-return-request" || rule.Threshold != WorkInboxRulesV1.Decimal(delivered)
            || rule.Actual != WorkInboxRulesV1.Decimal(quantity) || rule.Deviation is not null
            || rule.Unit != (facts.QuantityUnit == "Kilogram" ? "kg" : "adet")) return false;
        return true;
    }

    private async Task<WorkDecisionResultV1> DecideLogisticsCustomerReturnAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (logisticsCustomerReturns is null || snapshot.LogisticsCustomerReturnProof is not { Return: { } proof })
            throw new WorkDecisionException(503);
        CustomerReturnResponseV1 answer;
        try
        {
            var metadata = new LogisticsCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
            answer = request.Approve
                ? await logisticsCustomerReturns.ApproveAsync(id, new(request.ExpectedVersion, request.LogisticsCustomerReturn!.InventoryMovementId, request.Reason), metadata, token)
                : await logisticsCustomerReturns.RejectAsync(id, new(request.ExpectedVersion, request.Reason), metadata, token);
        }
        catch (LogisticsCustomerReturnServiceException error)
        { throw new WorkDecisionException(error.Status, error.Code, error.IsReplay, error.OriginalCorrelationId); }
        catch (LogisticsStoreException error)
        {
            // Same published native endpoint mapping; the adapter does not invent a terminal receipt.
            var refusal = error.Code switch
            {
                LogisticsStoreCodes.SameKeyPayloadMismatch => new WorkDecisionException(409, LogisticsProblemCodesV1.SameKeyPayloadMismatch),
                LogisticsStoreCodes.ConcurrentChange => new WorkDecisionException(409, LogisticsProblemCodesV1.ConcurrentChange),
                LogisticsStoreCodes.CompanyContextMismatch => new WorkDecisionException(403, "forbidden"),
                LogisticsStoreCodes.ValueDoesNotFit => new WorkDecisionException(422, "validation_failed"),
                LogisticsStoreCodes.AggregateLimitExceeded => new WorkDecisionException(409, LogisticsCustomerReturnCodesV1.QuantityExceeded),
                _ => new WorkDecisionException(503, "service_unavailable"),
            };
            throw refusal;
        }
        // Native FindReplay validates the original command/key actor+request hash and restores ResultJson,
        // not a fresh header. No separate Work receipt or mutable-current success inference is permitted.
        var result = answer?.Return;
        var state = request.Approve ? "Approved" : "Rejected";
        var note = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (answer is null || !answer.CommandReceiptProven || result is null || result.CompanyId != snapshot.CompanyId
            || result.CustomerReturnId != id || result.Version != (long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture) || result.Status != state
            || result.LoadingInstructionId != proof.LoadingInstructionId || result.LoadingInstructionLineId != proof.LoadingInstructionLineId
            || result.SourceDeliveryId != proof.SourceDeliveryId || result.SourceShipmentId != proof.SourceShipmentId
            || result.SourceOrderId != proof.SourceOrderId || result.SourceOrderLineId != proof.SourceOrderLineId || result.UnitId != proof.UnitId
            || result.Quantity != proof.Quantity || result.SourceDeliveredQuantity != proof.SourceDeliveredQuantity || result.Reason != proof.Reason
            || result.RequestedBy != proof.RequestedBy || result.RequestedAtUtc != proof.RequestedAtUtc || result.OccurredAtUtc != proof.OccurredAtUtc
            || !SameReturnWeighing(result.Weighing, proof.Weighing)
            || result.DecidedBy != actor || result.DecidedAtUtc is not { } decided || decided == default || decided.Offset != TimeSpan.Zero
            || result.DecisionNote != note || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
            || answer.OriginalCorrelationId.Trim() != answer.OriginalCorrelationId || answer.OriginalCorrelationId.Length > 128
            || !answer.IsReplay && (answer.OriginalCorrelationId != correlation.CorrelationId || !snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)
            || request.Approve && (result.InventoryMovementId != request.LogisticsCustomerReturn!.InventoryMovementId
                || result.OriginalLabelId is null || result.OriginalLabelId == Guid.Empty
                || result.OriginalShipmentMovementId is null || result.OriginalShipmentMovementId == Guid.Empty)
            || !request.Approve && (result.InventoryMovementId is not null || result.OriginalLabelId is not null || result.OriginalShipmentMovementId is not null))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, LogisticsCustomerReturn, id, state, answer.IsReplay, answer.OriginalCorrelationId);
    }

    private static bool SameReturnWeighing(CustomerReturnWeighingViewV1? native, WorkCustomerReturnWeighingV1? proof) =>
        native is null ? proof is null : proof is not null && native.WeighingId == proof.WeighingId && native.Direction == proof.Direction
            && native.FirstWeighingKg == proof.FirstWeighingKg && native.SecondWeighingKg == proof.SecondWeighingKg && native.NetKg == proof.NetKg
            && native.RecordedBy == proof.RecordedBy && native.RecordedAtUtc == proof.RecordedAtUtc;

    private static bool ValidReturnWeighing(WorkLogisticsCustomerReturnPreviewV1 facts)
    {
        if (facts.Weighing is not { } value) return true;
        if (facts.QuantityUnit != "Piece" || value.WeighingId == Guid.Empty || value.Direction is not ("TareFirst" or "GrossFirst")
            || string.IsNullOrWhiteSpace(value.RecordedBy) || value.RecordedBy != value.RecordedBy.Trim() || value.RecordedBy.Length > 128
            || value.RecordedAtUtc.Offset != TimeSpan.Zero) return false;
        // The same native WGT-001 calculator validates these stored facts; no conversion or tolerance is introduced.
        try
        {
            bool Weight(string text, out decimal number) => decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number)
                && number > 0 && number.ToString("0.###", CultureInfo.InvariantCulture) == text;
            if (!Weight(value.FirstWeighingKg, out var first) || !Weight(value.SecondWeighingKg, out var second) || !Weight(value.NetKg, out var net)) return false;
            return GoymenERP.Modules.Logistics.Domain.Loading.WeighbridgeCalculator.Net(
                Enum.Parse<GoymenERP.Modules.Logistics.Domain.Loading.WeighingDirection>(value.Direction),
                GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>.Positive(first, GoymenERP.BuildingBlocks.Quantities.QuantityUnit.Kilogram),
                GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>.Positive(second, GoymenERP.BuildingBlocks.Quantities.QuantityUnit.Kilogram)).Value == net;
        }
        catch (Exception error) when (error is ArgumentException or GoymenERP.Modules.Logistics.Domain.LogisticsRuleException) { return false; }
    }
}
