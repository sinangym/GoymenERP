using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidPostingAccountMappingRequest(string kind, WorkDecisionRequestV1 request) =>
        kind != PostingAccountMapping ? request.PostingAccountMappingSelection is null
        : request.Approve && request.Reason is null && request.PostingAccountMappingSelection is { } selection
            && CatalogVersion(request.ExpectedVersion, true, true, out _)
            // Active mapping ID is observed, not incremented by this command.
            && CatalogVersion(selection.ExpectedActiveMappingVersion, false, false, out _)
            && request.ApprovedSourceCatalogSelection is null && request.AccountingPeriodReopening is null && request.NormalJournalDraft is null
            && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null
            && request.LogisticsPlanExcess is null && request.InventoryTransferOverage is null
            && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null && request.ProductPackageStandard is null;
    private static bool ValidPostingAccountMappingProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot) =>
        kind != PostingAccountMapping ? snapshot.PostingAccountMappingProof is null
        : snapshot.PostingAccountMappingProof is { } p && p.CompanyId == snapshot.CompanyId
            && AccountingPostingAccountMappingDecisionBindingV1.TryMappingVersion(snapshot.Item.ItemId, out var nativeId) && nativeId == p.MappingVersion
            && p.Version > 0 && p.ActiveMappingVersion >= 0 && snapshot.SourceVersion == p.Version.ToString(CultureInfo.InvariantCulture)
            && snapshot.Pending == (p.Status == "Prepared")
            && AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionProof(snapshot.Item, p);
    private async Task PostingAccountMappingFenceAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(PostingAccountMapping, AccountingPostingAccountMappingPermissionNamesV1.Activate, snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }
    private async Task<WorkPostingAccountMappingPreviewV1> PostingAccountMappingHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (postingAccountMappings is null) throw new WorkDecisionException(503);
        var p = snapshot.PostingAccountMappingProof!;
        AccountingApiResult<AccountingPostingAccountMappingDecisionDetailV1> answer;
        try { answer = await postingAccountMappings.ReadDecisionAsync(p.MappingVersion, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.OriginalCorrelationId is not null
            || answer.Resource is not { } detail || detail.Mapping is not { } view
            || view.CompanyId != snapshot.CompanyId || view.MappingVersion != p.MappingVersion || view.Status != p.Status
            || view.PreparedByUserId != p.PreparedBy || view.PreparedAtUtc != p.PreparedAtUtc || view.Version != p.Version
            || detail.ExpectedVersion != p.Version || detail.ExpectedActiveMappingVersion != p.ActiveMappingVersion
            || detail.SourceFactsDigest != p.SourceFactsDigest || detail.PreviewDigest != p.PreviewDigest
            || !AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return WorkPostingAccountMappingPreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => PostingAccountMappingFenceAsync(snapshot, actor, ct);
    }
    private async Task<WorkDecisionResultV1> DecidePostingAccountMappingAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (postingAccountMappings is null) throw new WorkDecisionException(503);
        if (!AccountingPostingAccountMappingDecisionBindingV1.TryMappingVersion(id, out var mappingVersion)) throw new WorkDecisionException(503);
        var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        var selected = long.Parse(request.PostingAccountMappingSelection!.ExpectedActiveMappingVersion, CultureInfo.InvariantCulture);
        AccountingApiResult<AccountingPostingAccountMappingViewV1> answer;
        try { answer = await postingAccountMappings.ActivateAsync(mappingVersion, new(expected, selected), key, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await PostingAccountMappingFenceAsync(snapshot, actor, token);
        RequireAccountingPeriodAnswer(answer);
        var p = snapshot.PostingAccountMappingProof!;
        if (answer.Resource is not { } view || view.ActivationReceipt is not { } receipt
            || view.CompanyId != snapshot.CompanyId || view.MappingVersion != mappingVersion || view.Status != "Active" || view.Version != expected + 1
            || view.PreparedByUserId != p.PreparedBy || view.PreparedAtUtc != p.PreparedAtUtc
            || !Guid.TryParse(actor, out var checker) || checker == Guid.Empty || checker == view.PreparedByUserId || view.ActivatedByUserId != checker
            || view.ActivatedAtUtc is not { } at || at == default || at.Offset != TimeSpan.Zero
            || receipt.CompanyId != snapshot.CompanyId || receipt.MappingVersion != mappingVersion || receipt.ActorId != checker
            || receipt.CommandName != AccountingPostingAccountMappingCommandNamesV1.Activate
            || receipt.IdempotencyKeyHash != AccountingPostingAccountMappingServiceV1.ActivationKeyHash(key)
            || receipt.PayloadHash != AccountingPostingAccountMappingServiceV1.ActivationPayloadHash(mappingVersion, expected, selected)
            || receipt.ExpectedVersion != expected || receipt.ExpectedActiveMappingVersion != selected || receipt.ResultVersion != expected + 1
            || receipt.ActivatedAtUtc != at || receipt.SourceFactsDigest != p.SourceFactsDigest
            || receipt.OriginalResultDigest != AccountingPostingAccountMappingServiceV1.ActivationOriginalResultDigest(answer)
            || !NormalJournalDigest(receipt.OriginalResultDigest) || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || !AccountingPostingAccountMappingDecisionBindingV1.MatchesDecisionItem(snapshot.Item, view, receipt)
            || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId
                    || !snapshot.Pending || p.Version != expected || p.ActiveMappingVersion != selected)) throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, PostingAccountMapping, id, "Active", answer.IsReplay, receipt.OriginalCorrelationId);
    }
}
