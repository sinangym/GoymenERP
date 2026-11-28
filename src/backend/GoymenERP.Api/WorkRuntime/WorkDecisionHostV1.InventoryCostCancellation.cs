using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool InventoryCostCancellationVersion(string? text, out long version, bool increment = false) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0
        && (!increment || version < long.MaxValue) && text == version.ToString(CultureInfo.InvariantCulture);

    private static bool ValidInventoryCostCancellationRequest(string kind, WorkDecisionRequestV1 request) =>
        kind != InventoryCostCancellation ? request.InventoryCostCancellation is null
        : request.Approve && request.Reason is null
            && InventoryCostCancellationVersion(request.ExpectedVersion, out _, increment: true)
            && request.InventoryCostCancellation is { } selection
            && InventoryCostCancellationVersion(selection.ExpectedProposalVersion, out _)
            && request.PostingAccountMappingSelection is null && request.ApprovedSourceCatalogSelection is null
            && request.AccountingPeriodReopening is null && request.NormalJournalDraft is null && request.ProductPackageStandard is null
            && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null && request.InventoryTransferOverage is null
            && request.LogisticsPlanExcess is null && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null;

    private static bool ValidInventoryCostCancellationProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot) =>
        kind != InventoryCostCancellation ? snapshot.InventoryCostCancellationProof is null
        : snapshot.InventoryCostCancellationProof is { } p
            && p.CompanyId == snapshot.CompanyId && p.InventoryCostProposalId == snapshot.Item.ItemId
            && snapshot.SourceVersion == p.PreparedCancellationVersion.ToString(CultureInfo.InvariantCulture)
            && (!snapshot.Pending || p.Status == "Prepared")
            && AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionProof(snapshot.Item, p);

    private async Task InventoryCostCancellationFenceAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(InventoryCostCancellation, AccountingJournalReversalPermissionNamesV1.Approve,
            snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkInventoryCostCancellationPreviewV1> InventoryCostCancellationHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (inventoryCosts is null) throw new WorkDecisionException(503);
        var p = snapshot.InventoryCostCancellationProof!;
        AccountingApiResult<AccountingInventoryCostCancellationDecisionDetailV1> answer;
        try { answer = await inventoryCosts.ReadCancellationDecisionAsync(snapshot.Item.ItemId, p.PreparedCancellationVersion, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.OriginalCorrelationId is not null
            || answer.Resource is not { } d || !AccountingInventoryCostServiceV1.ValidCancellationDecisionDetail(d)
            || d.OriginalProposal.CompanyId != snapshot.CompanyId || d.OriginalProposal.ProposalId != p.InventoryCostProposalId
            || d.OriginalProposal.Version != p.ProposalVersion || d.PreparedCancellation.Id != p.ComponentCancellationId
            || d.PreparedCancellation.CancellationId != p.SourceCancellationId || d.PreparedCancellation.Version != p.PreparedCancellationVersion
            || d.PreparedCancellation.PeriodId != p.PeriodId || d.PreparedCancellation.PreparedBy != p.PreparedBy
            || d.PreparedCancellation.PreparedAtUtc != p.PreparedAtUtc || d.PreparedRecordedAtUtc != p.PreparedRecordedAtUtc
            || d.Cancellation.Status != p.Status || d.Pending != snapshot.Pending
            || d.SourceFactsDigest != p.SourceFactsDigest || d.PreviewDigest != p.PreviewDigest
            || !AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionItem(snapshot.Item, d)) throw new WorkDecisionException(503);
        return WorkInventoryCostCancellationPreviewV1.FromNative(d);
        Task Fence(CancellationToken ct) => InventoryCostCancellationFenceAsync(snapshot, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideInventoryCostCancellationAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (inventoryCosts is null) throw new WorkDecisionException(503);
        var p = snapshot.InventoryCostCancellationProof!;
        var expectedCancellation = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        var expectedProposal = long.Parse(request.InventoryCostCancellation!.ExpectedProposalVersion, CultureInfo.InvariantCulture);
        if (expectedProposal != p.ProposalVersion || expectedCancellation != p.PreparedCancellationVersion)
            throw new WorkDecisionException(409, "work_decision_version_conflict");
        var nativeRequest = new ApproveInventoryCostCancellationV1(expectedProposal, expectedCancellation);
        AccountingApiResult<AccountingInventoryCostProposalV1> answer;
        try { answer = await inventoryCosts.ApproveCancellationAsync(id, nativeRequest, key, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await InventoryCostCancellationFenceAsync(snapshot, actor, token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Resource is null) { RequireAccountingPeriodAnswer(answer); throw new WorkDecisionException(503); }
        if (answer.Status != AccountingApiStatus.Ok || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null
            || answer.Resource is not { ComponentCancellation: { } c, CancellationApprovalReceipt: { } receipt } v
            || v.CompanyId != snapshot.CompanyId || v.ProposalId != id || v.Version != expectedProposal
            || v.Status != "CancelledByComponentReversal" || c.CompanyId != snapshot.CompanyId || c.InventoryCostProposalId != id
            || c.Id != p.ComponentCancellationId || c.CancellationId != p.SourceCancellationId || c.Status != "Approved"
            || c.Version != expectedCancellation + 1 || c.PreparedBy != p.PreparedBy || c.PreparedAtUtc != p.PreparedAtUtc || c.PeriodId != p.PeriodId
            || !Guid.TryParse(actor, out var checker) || checker == Guid.Empty || checker == p.PreparedBy || c.ApprovedBy != checker
            || receipt.CompanyId != snapshot.CompanyId || receipt.InventoryCostProposalId != id
            || receipt.ComponentCancellationId != p.ComponentCancellationId || receipt.SourceCancellationId != p.SourceCancellationId
            || receipt.CommandName != AccountingInventoryCostServiceV1.InventoryCostCancellationApproveCommand
            || receipt.IdempotencyKeyHash != AccountingInventoryCostServiceV1.InventoryCostApprovalKeyHash(key)
            || receipt.PayloadHash != AccountingInventoryCostServiceV1.CancellationApprovalPayloadHash(id, nativeRequest)
            || receipt.ActorId != checker || receipt.ExpectedProposalVersion != expectedProposal
            || receipt.ExpectedCancellationVersion != expectedCancellation || receipt.ResultProposalVersion != expectedProposal
            || receipt.ResultCancellationVersion != expectedCancellation + 1 || receipt.PreparedBy != p.PreparedBy
            || receipt.PreparedAtUtc != p.PreparedAtUtc || receipt.PreparedRecordedAtUtc != p.PreparedRecordedAtUtc
            || receipt.DecidedAtUtc != c.ApprovedAtUtc || receipt.SourceFactsDigest != p.SourceFactsDigest
            || receipt.OriginalResultDigest != AccountingInventoryCostServiceV1.InventoryCostOriginalResultDigest(answer)
            || !NormalJournalDigest(receipt.OriginalResultDigest) || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || !AccountingInventoryCostCancellationDecisionBindingV1.MatchesDecisionItem(snapshot.Item, v, receipt)
            || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId || !snapshot.Pending))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, InventoryCostCancellation, id, v.Status, answer.IsReplay, receipt.OriginalCorrelationId);
    }
}
