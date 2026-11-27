using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool CatalogVersion(string? text, bool positive, bool increment, out long version) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version)
        && (positive ? version > 0 : version >= 0) && (!increment || version < long.MaxValue)
        && text == version.ToString(CultureInfo.InvariantCulture);

    private static bool ValidApprovedSourceCatalogRequest(string kind, WorkDecisionRequestV1 request) =>
        kind != ApprovedSourceCatalog ? request.ApprovedSourceCatalogSelection is null
        : request.Approve && request.Reason is null && request.ApprovedSourceCatalogSelection is { } selection
            && CatalogVersion(request.ExpectedVersion, true, true, out _)
            && CatalogVersion(selection.ExpectedActiveCatalogVersion, false, true, out _)
            && request.AccountingPeriodReopening is null && request.NormalJournalDraft is null
            && request.ManufacturingPlanReapproval is null && request.InventoryTransferLoss is null
            && request.LogisticsPlanExcess is null && request.InventoryTransferOverage is null
            && request.SupplierReturnCostException is null && request.LogisticsCustomerReturn is null
            && request.ProductPackageStandard is null;

    private static bool ValidApprovedSourceCatalogProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot) =>
        snapshot.PostingAccountMappingProof is null && (kind != ApprovedSourceCatalog ? snapshot.ApprovedSourceCatalogProof is null
        : snapshot.ApprovedSourceCatalogProof is { } p && p.CompanyId == snapshot.CompanyId
            && p.PackageId == snapshot.Item.ItemId && p.PackageVersion > 0 && p.ActiveCatalogVersion >= 0
            && snapshot.SourceVersion == p.PackageVersion.ToString(CultureInfo.InvariantCulture)
            && snapshot.Pending == (p.Status == "Prepared")
            && AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionProof(snapshot.Item, p));

    private async Task ApprovedSourceCatalogFenceAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(ApprovedSourceCatalog, ApprovedAccountingSourceCatalogPermissionNamesV1.Activate,
            snapshot.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }

    private async Task<WorkApprovedSourceCatalogPreviewV1> ApprovedSourceCatalogHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (approvedSourceCatalogs is null) throw new WorkDecisionException(503);
        AccountingApiResult<ApprovedAccountingSourceCatalogDecisionDetailV1> answer;
        try { answer = await approvedSourceCatalogs.ReadDecisionAsync(snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.ApprovedSourceCatalogProof!;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.OriginalCorrelationId is not null
            || answer.Resource is not { } detail || detail.Catalog is not { } view
            || view.CompanyId != snapshot.CompanyId || view.PackageId != p.PackageId || view.Status != p.Status
            || view.PreparedByUserId != p.PreparedBy || view.PreparedAtUtc != p.PreparedAtUtc || view.Version != p.PackageVersion
            || detail.ExpectedPackageVersion != p.PackageVersion || detail.ExpectedActiveCatalogVersion != p.ActiveCatalogVersion
            || detail.SourceFactsDigest != p.SourceFactsDigest || detail.PreviewDigest != p.PreviewDigest
            || !AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionItem(snapshot.Item, detail))
            throw new WorkDecisionException(503);
        return WorkApprovedSourceCatalogPreviewV1.FromNative(detail);
        Task Fence(CancellationToken ct) => ApprovedSourceCatalogFenceAsync(snapshot, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideApprovedSourceCatalogAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (approvedSourceCatalogs is null) throw new WorkDecisionException(503);
        var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
        var selected = long.Parse(request.ApprovedSourceCatalogSelection!.ExpectedActiveCatalogVersion, CultureInfo.InvariantCulture);
        AccountingApiResult<ApprovedAccountingSourceCatalogViewV1> answer;
        try { answer = await approvedSourceCatalogs.ActivateAsync(id, new(expected, selected), key, token); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await ApprovedSourceCatalogFenceAsync(snapshot, actor, token);
        RequireAccountingPeriodAnswer(answer);
        var p = snapshot.ApprovedSourceCatalogProof!;
        if (answer.Resource is not { } view || view.ActivationReceipt is not { } receipt
            || view.CompanyId != snapshot.CompanyId || view.PackageId != id || view.Status != "Active"
            || view.Version != expected + 1 || view.CatalogVersion != selected + 1
            || view.PreparedByUserId != p.PreparedBy || view.PreparedAtUtc != p.PreparedAtUtc
            || !Guid.TryParse(actor, out var checker) || checker == Guid.Empty || view.ActivatedByUserId != checker
            || view.ActivatedAtUtc is not { } at || at == default || at.Offset != TimeSpan.Zero
            || receipt.CompanyId != snapshot.CompanyId || receipt.PackageId != id || receipt.ActorId != checker
            || receipt.CommandName != ApprovedAccountingSourceCatalogCommandNamesV1.Activate
            || receipt.IdempotencyKeyHash != ApprovedAccountingSourceCatalogServiceV1.ActivationKeyHash(key)
            || receipt.PayloadHash != ApprovedAccountingSourceCatalogServiceV1.ActivationPayloadHash(id, expected, selected)
            || receipt.ExpectedPackageVersion != expected || receipt.ExpectedActiveCatalogVersion != selected
            || receipt.ResultVersion != expected + 1 || receipt.CatalogVersion != selected + 1 || receipt.ActivatedAtUtc != at
            || receipt.SourceFactsDigest != p.SourceFactsDigest
            || receipt.OriginalResultDigest != ApprovedAccountingSourceCatalogServiceV1.ActivationOriginalResultDigest(answer)
            || !NormalJournalDigest(receipt.OriginalResultDigest) || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || !AccountingApprovedSourceCatalogDecisionBindingV1.MatchesDecisionItem(snapshot.Item, view, receipt)
            || (answer.IsReplay ? answer.OriginalCorrelationId != receipt.OriginalCorrelationId
                : answer.OriginalCorrelationId is not null || receipt.OriginalCorrelationId != correlation.CorrelationId
                    || !snapshot.Pending || p.PackageVersion != expected || p.ActiveCatalogVersion != selected))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, ApprovedSourceCatalog, id, "Active", answer.IsReplay, receipt.OriginalCorrelationId);
    }
}
