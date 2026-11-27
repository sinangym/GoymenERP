using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidManufacturingCostDifferenceRequest(string kind, WorkDecisionRequestV1 request) =>
        kind != ManufacturingCostDifference || request.Approve && request.Reason is null
            && CatalogVersion(request.ExpectedVersion, true, true, out _)
            && request.PostingAccountMappingSelection is null && request.ApprovedSourceCatalogSelection is null
            && request.AccountingPeriodReopening is null && request.NormalJournalDraft is null && request.ProductPackageStandard is null
            && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null && request.InventoryTransferOverage is null
            && request.LogisticsPlanExcess is null && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null;

    private static bool ValidManufacturingCostDifferenceProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot) =>
        kind != ManufacturingCostDifference ? snapshot.ManufacturingCostDifferenceProof is null
        : snapshot.ManufacturingCostDifferenceProof is { } p && p.CompanyId == snapshot.CompanyId && p.ProposalId == snapshot.Item.ItemId
            && CatalogVersion(snapshot.SourceVersion, true, false, out var version) && version >= 2
            && snapshot.Pending == (p.Status == "Prepared")
            && AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionProof(snapshot.Item, p);

    private async Task ManufacturingCostDifferenceFenceAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(ManufacturingCostDifference, AccountingJournalReversalPermissionNamesV1.Approve, snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkManufacturingCostDifferencePreviewV1> ManufacturingCostDifferenceHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (manufacturingCostDifferences is null) throw new WorkDecisionException(503);
        AccountingApiResult<ManufacturingCostDifferenceDecisionDetailV1> answer;
        try { answer = await manufacturingCostDifferences.ReadDecisionAsync(snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.ManufacturingCostDifferenceProof!;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.OriginalCorrelationId is not null
            || answer.Resource is not { } detail || detail.Proposal is not { } view
            || view.CompanyId != snapshot.CompanyId || view.ProposalId != p.ProposalId || view.ManufacturingOrderId != p.ManufacturingOrderId
            || view.Status != p.Status || view.Version.ToString(CultureInfo.InvariantCulture) != snapshot.SourceVersion
            || view.PeriodId != p.PeriodId || view.DebitAccountId != p.DebitAccountId || view.CreditAccountId != p.CreditAccountId
            || view.PreparedBy != p.PreparedBy || detail.PreparedAtUtc != p.PreparedAtUtc
            || detail.SourceFactsDigest != p.SourceFactsDigest || detail.PreviewDigest != p.PreviewDigest
            || !AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return WorkManufacturingCostDifferencePreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => ManufacturingCostDifferenceFenceAsync(snapshot, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideManufacturingCostDifferenceAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (manufacturingCostDifferences is null) throw new WorkDecisionException(503);
        var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        AccountingApiResult<ManufacturingCostDifferenceViewV1> answer;
        try { answer = await manufacturingCostDifferences.ApproveAsync(id, expected, key, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await ManufacturingCostDifferenceFenceAsync(snapshot, actor, token);
        RequireAccountingPeriodAnswer(answer);
        var p = snapshot.ManufacturingCostDifferenceProof!;
        if (answer.Resource is not { } view || view.ApprovalReceipt is not { } receipt
            || view.CompanyId != snapshot.CompanyId || view.ProposalId != id || view.Status != "Posted" || view.Version != expected + 1
            || view.ManufacturingOrderId != p.ManufacturingOrderId || view.PeriodId != p.PeriodId
            || view.DebitAccountId != p.DebitAccountId || view.CreditAccountId != p.CreditAccountId || view.PreparedBy != p.PreparedBy
            || !Guid.TryParse(actor, out var checker) || checker == Guid.Empty || checker == p.PreparedBy || view.ApprovedBy != checker
            || view.JournalId is not { } journal || journal == Guid.Empty
            || receipt.CompanyId != snapshot.CompanyId || receipt.ProposalId != id || receipt.JournalId != journal
            || receipt.PreparedBy != p.PreparedBy || receipt.PreparedAtUtc != p.PreparedAtUtc || receipt.ActorId != checker
            || receipt.CommandName != ManufacturingCostDifferenceCommandNamesV1.Approve
            || receipt.IdempotencyKeyHash != ManufacturingCostDifferenceServiceV1.CostDifferenceApprovalKeyHash(key)
            || receipt.PayloadHash != ManufacturingCostDifferenceServiceV1.CostDifferenceApprovalPayloadHash(id, expected)
            || receipt.ExpectedVersion != expected || receipt.ResultVersion != expected + 1
            || receipt.SourceFactsDigest != p.SourceFactsDigest
            || receipt.OriginalResultDigest != ManufacturingCostDifferenceServiceV1.CostDifferenceOriginalResultDigest(answer)
            || !NormalJournalDigest(receipt.OriginalResultDigest) || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || !AccountingManufacturingCostDifferenceDecisionBindingV1.MatchesDecisionItem(snapshot.Item, view, receipt)
            || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId
                    || !snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion)) throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, ManufacturingCostDifference, id, "Posted", answer.IsReplay, receipt.OriginalCorrelationId);
    }
}
