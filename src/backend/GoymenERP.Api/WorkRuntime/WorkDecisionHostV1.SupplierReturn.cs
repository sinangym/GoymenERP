using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Human-only retained facts. Costs are present only under the owner's existing disclosure grant.</summary>
public sealed record WorkSupplierReturnCostExceptionPreviewV1(Guid PurchaseOrderId, Guid PurchaseOrderLineId,
    Guid GoodsAcceptanceId, Guid InventoryMovementId, string SourceBlock, string QuantityKg, bool ReplacementExpected,
    string Reason, DateTimeOffset OccurredAtUtc, string RequestedBy, DateTimeOffset RequestedAtUtc,
    string? RecordedExpectedPurchaseOrderVersion, string? CurrentPurchaseOrderVersion, string? UndoQuantity,
    string PriceDisclosure, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UnitCostTryPerKg);

public sealed partial class WorkDecisionHostV1
{
    private static bool ReturnVersion(string? text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value > 0 && value < long.MaxValue && value.ToString(CultureInfo.InvariantCulture) == text;

    private static bool ValidSupplierReturnProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        var p = snapshot.SupplierReturnCostExceptionProof;
        if (kind != SupplierReturnCostException) return p is null;
        return p is not null && p.CompanyId == snapshot.CompanyId && p.ReturnId == snapshot.Item.ItemId
            && p.ReturnLineId != Guid.Empty && p.PurchaseOrderId != Guid.Empty && p.PurchaseOrderLineId != Guid.Empty
            && p.GoodsAcceptanceId != Guid.Empty && p.InventoryMovementId != Guid.Empty
            && p.SourceBlock is ("Free" or "Blocked") && PurchaseSupplierReturnJsonV1.IsPositiveQuantity(p.QuantityKg)
            && p.CostBasis == "ManualCostWithException" && !string.IsNullOrWhiteSpace(p.Reason) && p.Reason.Length <= 500
            && p.RequestedBy == snapshot.Item.PreparedBy && p.RequestedAtUtc == snapshot.Item.WaitingSinceUtc
            && p.OccurredAtUtc != default && p.OccurredAtUtc.Offset == TimeSpan.Zero
            && p.RequestedAtUtc != default && p.RequestedAtUtc.Offset == TimeSpan.Zero
            && snapshot.Item.Rules.Count == 2
            && snapshot.Item.Rules.Count(r => r.RuleCode == "purchasing.supplier-return-manual-cost" && r.Threshold is null && r.Deviation is null
                && (r.Actual is null ? r.Unit is null : r.Unit == "TRY" && PurchaseSupplierReturnJsonV1.IsPositiveUnitCost(r.Actual))) == 1
            && snapshot.Item.Rules.Count(r => r.RuleCode == "purchasing.supplier-return-quantity" && r.Actual == p.QuantityKg
                && r.Unit == "kg" && r.Threshold is null && r.Deviation is null) == 1
            && ReturnVersion(snapshot.SourceVersion)
            && snapshot.Item.References.Count(r => r.Role == "purchase-order" && r.RecordId == p.PurchaseOrderId) == 1
            && (p.RecordedExpectedPurchaseOrderVersion is null ? p.CurrentPurchaseOrderVersion is null && p.UndoQuantity is null
                : ReturnVersion(p.RecordedExpectedPurchaseOrderVersion) && ReturnVersion(p.CurrentPurchaseOrderVersion)
                  && PurchaseSupplierReturnJsonV1.IsPositiveQuantity(p.UndoQuantity));
    }

    private static bool ValidSupplierReturnRequest(WorkApprovalDecisionSnapshotV1 snapshot, WorkDecisionRequestV1 request)
    {
        if (!ReturnVersion(request.ExpectedVersion)) return false;
        var selected = request.SupplierReturnCostException?.ExpectedPurchaseOrderVersion;
        if (request.Approve && snapshot.SupplierReturnCostExceptionProof!.RecordedExpectedPurchaseOrderVersion is not null)
        { if (!ReturnVersion(selected)) return false; }
        else if (selected is not null) return false;
        try { PurchaseSupplierReturnJsonV1.ValidateDecision(new(request.ExpectedVersion, request.Reason, selected)); }
        catch (JsonException) { return false; }
        return request.Approve || !string.IsNullOrWhiteSpace(request.Reason);
    }

    private static WorkSupplierReturnCostExceptionPreviewV1 SupplierReturnHumanPreview(WorkApprovalDecisionSnapshotV1 snapshot)
    {
        var p = snapshot.SupplierReturnCostExceptionProof!;
        var cost = snapshot.Item.Rules.SingleOrDefault(r => r.RuleCode == "purchasing.supplier-return-manual-cost")?.Actual;
        return new(p.PurchaseOrderId, p.PurchaseOrderLineId, p.GoodsAcceptanceId, p.InventoryMovementId, p.SourceBlock, p.QuantityKg,
            p.ReplacementExpected, p.Reason, p.OccurredAtUtc, p.RequestedBy, p.RequestedAtUtc, p.RecordedExpectedPurchaseOrderVersion,
            p.CurrentPurchaseOrderVersion, p.UndoQuantity, cost is null ? PurchasingPriceDisclosureV1.Withheld : PurchasingPriceDisclosureV1.Visible, cost);
    }

    private async Task<WorkDecisionResultV1> DecideSupplierReturnAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (supplierReturns is null) throw new WorkDecisionException(503);
        var p = snapshot.SupplierReturnCostExceptionProof!;
        var body = new DecidePurchaseSupplierReturnV1(request.ExpectedVersion, request.Reason, request.SupplierReturnCostException?.ExpectedPurchaseOrderVersion);
        try
        {
            var metadata = new PurchasingCommandMetadata(key, correlation.CorrelationId, correlation.CausationId, DateTimeOffset.UtcNow);
            var answer = request.Approve
                ? await supplierReturns.ApproveSupplierReturnCostExceptionAsync(p.PurchaseOrderId, id, body, metadata, token)
                : await supplierReturns.RejectSupplierReturnCostExceptionAsync(p.PurchaseOrderId, id, body, metadata, token);
            if (answer?.Return is not { } r || !answer.CommandReceiptProven || r.CompanyId != snapshot.CompanyId || r.ReturnId != id
                || r.ReturnLineId != p.ReturnLineId || r.PurchaseOrderId != p.PurchaseOrderId || r.PurchaseOrderLineId != p.PurchaseOrderLineId
                || r.GoodsAcceptanceId != p.GoodsAcceptanceId || r.InventoryMovementId != p.InventoryMovementId
                || r.SourceBlock != p.SourceBlock || r.QuantityKg != p.QuantityKg || r.CostBasis != p.CostBasis
                || r.ReplacementExpected != p.ReplacementExpected || r.Reason != p.Reason || r.OccurredAtUtc != p.OccurredAtUtc
                || r.RequestedBy != p.RequestedBy || r.RequestedAtUtc != p.RequestedAtUtc
                || r.ExpectedPurchaseOrderVersion != p.RecordedExpectedPurchaseOrderVersion || r.UndoQuantity != p.UndoQuantity
                || r.Status != (request.Approve ? "Recorded" : "Rejected")
                || r.Version != (long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture)
                || !answer.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)
                || r.DecidedBy != actor || r.DecidedAtUtc is null || r.DecidedAtUtc == default(DateTimeOffset) || r.DecidedAtUtc.Value.Offset != TimeSpan.Zero
                || (request.Approve ? r.DecisionReason is not null : r.DecisionReason != request.Reason?.Trim())
                || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId) || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId
                || r.PriceDisclosure is not (PurchasingPriceDisclosureV1.Visible or PurchasingPriceDisclosureV1.Withheld)
                || r.PriceDisclosure == PurchasingPriceDisclosureV1.Withheld && (r.UnitCostTryPerKg is not null
                    || r.LineageParts?.Any(part => part.UnitCostTryPerKg is not null || part.SupplierUnitPriceTry is not null || part.SupplierAmountTry is not null) == true))
                throw new WorkDecisionException(503);
            return new(snapshot.CompanyId, SupplierReturnCostException, id, r.Status, answer.IsReplay, answer.OriginalCorrelationId);
        }
        catch (PurchasingServiceException error)
        { throw new WorkDecisionException(error.Status, error.Code, error.IsReplay, error.OriginalCorrelationId); }
    }
}
