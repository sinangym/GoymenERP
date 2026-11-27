using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool IsInventoryCostKind(string kind) => kind is InvoicedShipmentCost or PurchaseCostDifference;
    private static AccountingInventoryCostKindV1 InventoryCostKind(string kind) => kind == InvoicedShipmentCost
        ? AccountingInventoryCostKindV1.SalesInvoicedShipment : AccountingInventoryCostKindV1.PurchaseInvoiceDifference;
    private static string InventoryCostPermission(string kind) => kind == InvoicedShipmentCost
        ? NormalJournalPermissionNamesV1.DraftApprove : AccountingJournalReversalPermissionNamesV1.Approve;
    private static bool InventoryCostVersion(string? text, out long version, bool increment = false) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= 2
        && (!increment || version < long.MaxValue) && text == version.ToString(CultureInfo.InvariantCulture);
    private static bool ValidInventoryCostRequest(string kind, WorkDecisionRequestV1 request) => !IsInventoryCostKind(kind)
        || request.Approve && request.Reason is null && InventoryCostVersion(request.ExpectedVersion, out _, increment: true)
            && request.PostingAccountMappingSelection is null && request.ApprovedSourceCatalogSelection is null
            && request.AccountingPeriodReopening is null && request.NormalJournalDraft is null && request.ProductPackageStandard is null
            && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null && request.InventoryTransferOverage is null
            && request.LogisticsPlanExcess is null && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null;
    private static bool ValidInventoryCostProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot) => !IsInventoryCostKind(kind)
        ? snapshot.InventoryCostProof is null
        : snapshot.InventoryCostProof is { } p && p.CompanyId == snapshot.CompanyId && p.ProposalId == snapshot.Item.ItemId
            && p.CostKind == (int)InventoryCostKind(kind) && InventoryCostVersion(snapshot.SourceVersion, out _)
            && snapshot.Pending == (p.Status == "Prepared" && !p.SourceCancelled)
            && AccountingInventoryCostDecisionBindingV1.MatchesDecisionProof(snapshot.Item, p);
    private async Task InventoryCostFenceAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(snapshot.Item.Kind, InventoryCostPermission(snapshot.Item.Kind), snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }
    private async Task<WorkInventoryCostPreviewV1> InventoryCostHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (inventoryCosts is null) throw new WorkDecisionException(503);
        AccountingApiResult<AccountingInventoryCostDecisionDetailV1> answer;
        try { answer = await inventoryCosts.ReadDecisionAsync(snapshot.Item.ItemId, InventoryCostKind(snapshot.Item.Kind), Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.InventoryCostProof!;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.OriginalCorrelationId is not null
            || answer.Resource is not { } detail || detail.Proposal is not { Preparation: { } prep } v
            || v.CompanyId != snapshot.CompanyId || v.ProposalId != p.ProposalId || (int)v.Kind != p.CostKind
            || v.EventId != p.EventId || v.InvoiceId != p.InvoiceId || v.OfficialInvoiceNumber != p.OfficialInvoiceNumber
            || prep.PeriodId != p.PeriodId || prep.PreparedBy != p.PreparedBy || detail.PreparedRecordedAtUtc != p.PreparedRecordedAtUtc
            || v.Status != p.Status || v.Version.ToString(CultureInfo.InvariantCulture) != snapshot.SourceVersion
            || detail.SourceCancelled != p.SourceCancelled || snapshot.Pending != (v.Status == "Prepared" && detail.SourceCancelled == false)
            || detail.SourceFactsDigest != p.SourceFactsDigest || detail.PreviewDigest != p.PreviewDigest
            || !AccountingInventoryCostDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return WorkInventoryCostPreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => InventoryCostFenceAsync(snapshot, actor, ct);
    }
    private async Task<WorkDecisionResultV1> DecideInventoryCostAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (inventoryCosts is null) throw new WorkDecisionException(503);
        var p = snapshot.InventoryCostProof!; var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        var kind = InventoryCostKind(snapshot.Item.Kind);
        AccountingApiResult<AccountingInventoryCostProposalV1> answer;
        try { answer = await inventoryCosts.ApproveAsync(id, kind, expected, key, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await InventoryCostFenceAsync(snapshot, actor, token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Resource is null) { RequireAccountingPeriodAnswer(answer); throw new WorkDecisionException(503); }
        // A genuine native Stale closure may have committed effects. Authenticate it before releasing its original 409.
        if (answer.Status is not (AccountingApiStatus.Ok or AccountingApiStatus.Conflict)
            || answer.Resource is not { Preparation: { } prep } v || v.ApprovalReceipt is not { } receipt
            || v.Status is not ("Posted" or "RoundedNoCost" or "Stale") || v.CompanyId != snapshot.CompanyId || v.ProposalId != id
            || v.Kind != kind || v.EventId != p.EventId || v.InvoiceId != p.InvoiceId || v.OfficialInvoiceNumber != p.OfficialInvoiceNumber
            || prep.PreparedBy != p.PreparedBy || prep.PeriodId != p.PeriodId
            || !Guid.TryParse(actor, out var checker) || checker == Guid.Empty || checker == p.PreparedBy
            || receipt.CompanyId != snapshot.CompanyId || receipt.ProposalId != id || receipt.Kind != kind
            || receipt.EventId != p.EventId || receipt.InvoiceId != p.InvoiceId || receipt.PreparedBy != p.PreparedBy
            || receipt.PreparedAtUtc != prep.PreparedAtUtc || receipt.PreparedRecordedAtUtc != p.PreparedRecordedAtUtc || receipt.ActorId != checker
            || receipt.CommandName != AccountingInventoryCostServiceV1.InventoryCostApproveCommand
            || receipt.IdempotencyKeyHash != AccountingInventoryCostServiceV1.InventoryCostApprovalKeyHash(key)
            || receipt.PayloadHash != AccountingInventoryCostServiceV1.InventoryCostApprovalPayloadHash(id, kind, expected)
            || receipt.ExpectedVersion != expected || receipt.ResultVersion != expected + 1 || v.Version != receipt.ResultVersion
            || receipt.ResultStatus != v.Status || receipt.SourceFactsDigest != p.SourceFactsDigest
            || receipt.OriginalResultDigest != AccountingInventoryCostServiceV1.InventoryCostOriginalResultDigest(answer)
            || !NormalJournalDigest(receipt.OriginalResultDigest) || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || (v.Status == "Stale" ? answer.Status != AccountingApiStatus.Conflict || answer.Code != AccountingCostProposalStaleEventV1.ReasonCode
                : answer.Status != AccountingApiStatus.Ok || answer.Code is not null)
            || answer.Field is not null || answer.ExpectedVersion is not null || answer.CurrentVersion is not null
            || !AccountingInventoryCostDecisionBindingV1.MatchesDecisionItem(snapshot.Item, v, receipt)
            || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId
                    || !snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)) throw new WorkDecisionException(503);
        RequireAccountingPeriodAnswer(answer);
        return new(snapshot.CompanyId, snapshot.Item.Kind, id, v.Status, answer.IsReplay, receipt.OriginalCorrelationId);
    }
}
