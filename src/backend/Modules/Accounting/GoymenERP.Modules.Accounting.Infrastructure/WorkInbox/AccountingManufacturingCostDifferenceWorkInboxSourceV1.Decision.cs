using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal sealed partial class AccountingManufacturingCostDifferenceWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is null) throw new InvalidOperationException("Manufacturing cost decision owner unavailable.");
        var answer = await owner.ReadDecisionAsync(itemId, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Manufacturing cost decision answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.OriginalCorrelationId is not null || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null || answer.Resource is not { } detail || detail.Proposal is not { } view
            || view.CompanyId != context.CompanyId || view.ProposalId != itemId || !ManufacturingCostDifferenceServiceV1.ValidCostDifferenceDecisionDetail(detail)
            || AccountingManufacturingCostDifferenceDecisionBindingV1.FromView(view, detail.PreparedAtUtc) is not { } item)
            throw new InvalidOperationException("Manufacturing cost decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, view.Version.ToString(CultureInfo.InvariantCulture), view.Status == "Prepared")
        {
            ManufacturingCostDifferenceProof = new(context.CompanyId, itemId, view.ManufacturingOrderId, view.PeriodId!.Value,
                view.DebitAccountId!.Value, view.CreditAccountId!.Value, view.PreparedBy!.Value, detail.PreparedAtUtc,
                view.Status, detail.SourceFactsDigest, detail.PreviewDigest),
        };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Manufacturing cost decision authority unavailable.");
        }
    }
}
