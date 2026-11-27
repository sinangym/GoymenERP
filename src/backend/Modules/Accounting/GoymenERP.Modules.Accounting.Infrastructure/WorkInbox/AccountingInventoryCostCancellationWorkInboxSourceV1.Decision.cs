using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
internal sealed partial class AccountingInventoryCostCancellationWorkInboxSourceV1 : IWorkApprovalDecisionVersionSourceV1
{
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) =>
        Read(context, itemId, null, token);
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid itemId, string expectedProposalVersion, CancellationToken token)
    {
        if (!long.TryParse(expectedProposalVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version <= 0
            || version.ToString(CultureInfo.InvariantCulture) != expectedProposalVersion) throw new InvalidOperationException("Cancellation version unavailable.");
        return Read(context, itemId, version, token);
    }
    private async Task<WorkApprovalDecisionSnapshotV1?> Read(WorkInboxSourceContextV1 context, Guid itemId, long? version, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (decisionReader is null) throw new InvalidOperationException("Cancellation decision owner unavailable.");
        var answer = await decisionReader.ReadCancellationDecisionAsync(itemId, version, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Cancellation decision answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.OriginalCorrelationId is not null || answer.Code is not null
            || answer.Field is not null || answer.ExpectedVersion is not null || answer.CurrentVersion is not null
            || answer.Resource is not { } d || !AccountingInventoryCostServiceV1.ValidCancellationDecisionDetail(d)
            || d.OriginalProposal.CompanyId != context.CompanyId || d.OriginalProposal.ProposalId != itemId
            || version is { } selected && d.PreparedCancellation.Version != selected
            || AccountingInventoryCostCancellationDecisionBindingV1.Item(itemId, d.PreparedCancellation.CancellationId, d.PreparedCancellation.PeriodId,
                d.PreparedCancellation.PreparedBy, d.PreparedRecordedAtUtc) is not { } item)
            throw new InvalidOperationException("Cancellation decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        var before = d.PreparedCancellation;
        return new(context.CompanyId, item, before.Version.ToString(CultureInfo.InvariantCulture), d.Pending!.Value)
        {
            InventoryCostCancellationProof = new(context.CompanyId, itemId, before.Id, before.CancellationId, d.OriginalProposal.Version,
                before.Version, before.PeriodId, before.PreparedBy, before.PreparedAtUtc, d.PreparedRecordedAtUtc, d.Cancellation.Status, d.SourceFactsDigest, d.PreviewDigest),
        };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Cancellation decision authority unavailable.");
        }
    }
}
