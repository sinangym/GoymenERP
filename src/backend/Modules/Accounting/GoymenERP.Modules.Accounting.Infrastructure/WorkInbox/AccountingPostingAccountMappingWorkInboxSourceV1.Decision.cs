using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal sealed partial class AccountingPostingAccountMappingWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (!AccountingPostingAccountMappingDecisionBindingV1.TryMappingVersion(itemId, out var mappingVersion)) return null;
        if (owner is null) throw new InvalidOperationException("Mapping activation decision owner unavailable.");
        var answer = await owner.ReadDecisionAsync(mappingVersion, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Mapping activation decision answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.OriginalCorrelationId is not null || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.Resource is not { } detail || detail.Mapping is not { } view
            || view.CompanyId != context.CompanyId || view.MappingVersion != mappingVersion || !AccountingPostingAccountMappingServiceV1.ValidMappingDecisionDetail(detail)
            || AccountingPostingAccountMappingDecisionBindingV1.Item(view.MappingVersion, view.PreparedByUserId, view.PreparedAtUtc) is not { } item)
            throw new InvalidOperationException("Mapping activation decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException(); // Native mapping maker-checker, including terminal reads.
        return new(context.CompanyId, item, view.Version.ToString(CultureInfo.InvariantCulture), view.Status == "Prepared")
        {
            PostingAccountMappingProof = new(context.CompanyId, view.MappingVersion, view.PreparedByUserId, view.PreparedAtUtc, view.Status,
                detail.ExpectedVersion, detail.ExpectedActiveMappingVersion, detail.SourceFactsDigest, detail.PreviewDigest),
        };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Mapping activation decision authority unavailable.");
        }
    }
}
