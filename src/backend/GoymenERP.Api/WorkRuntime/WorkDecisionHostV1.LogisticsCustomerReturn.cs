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
            || facts.OccurredAtUtc == default || facts.OccurredAtUtc.Offset != TimeSpan.Zero
            || snapshot.Pending != (facts.Status == "Pending") || snapshot.SourceVersion != (snapshot.Pending ? "1" : "2")
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
            || result.CustomerReturnId != id || result.Version != "2" || request.ExpectedVersion != "1" || result.Status != state
            || result.LoadingInstructionId != proof.LoadingInstructionId || result.LoadingInstructionLineId != proof.LoadingInstructionLineId
            || result.SourceDeliveryId != proof.SourceDeliveryId || result.SourceShipmentId != proof.SourceShipmentId
            || result.SourceOrderId != proof.SourceOrderId || result.SourceOrderLineId != proof.SourceOrderLineId || result.UnitId != proof.UnitId
            || result.Quantity != proof.Quantity || result.SourceDeliveredQuantity != proof.SourceDeliveredQuantity || result.Reason != proof.Reason
            || result.RequestedBy != proof.RequestedBy || result.RequestedAtUtc != proof.RequestedAtUtc || result.OccurredAtUtc != proof.OccurredAtUtc
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
}
