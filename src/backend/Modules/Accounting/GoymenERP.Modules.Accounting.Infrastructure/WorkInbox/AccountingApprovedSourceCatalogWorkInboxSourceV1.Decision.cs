using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal sealed partial class AccountingApprovedSourceCatalogWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is null) throw new InvalidOperationException("Catalog activation decision owner unavailable.");
        var answer = await owner.ReadDecisionAsync(itemId, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Catalog activation decision answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.OriginalCorrelationId is not null || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.Resource is not { } detail || detail.Catalog is not { } view
            || view.CompanyId != context.CompanyId || view.PackageId != itemId || !ApprovedAccountingSourceCatalogServiceV1.ValidCatalogDecisionDetail(detail)
            || AccountingApprovedSourceCatalogDecisionBindingV1.Item(view.PackageId, view.PreparedByUserId, view.PreparedAtUtc) is not { } item)
            throw new InvalidOperationException("Catalog activation decision facts unavailable.");
        // Native activation explicitly permits the preparer; preserve maker metadata, no additional exclusion.
        return new(context.CompanyId, item, view.Version.ToString(CultureInfo.InvariantCulture), view.Status == "Prepared")
        {
            ApprovedSourceCatalogProof = new(context.CompanyId, itemId, view.PreparedByUserId, view.PreparedAtUtc, view.Status,
                detail.ExpectedPackageVersion, detail.ExpectedActiveCatalogVersion, detail.SourceFactsDigest, detail.PreviewDigest),
        };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Catalog activation decision authority unavailable.");
        }
    }
}
