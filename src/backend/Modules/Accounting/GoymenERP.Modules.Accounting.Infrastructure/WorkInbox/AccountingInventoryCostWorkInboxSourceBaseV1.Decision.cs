using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal abstract partial class AccountingInventoryCostWorkInboxSourceBaseV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (decisionReader is null) throw new InvalidOperationException("Inventory cost decision owner unavailable.");
        var answer = await decisionReader.ReadDecisionAsync(itemId, CostKind, Fence, token); await Fence(token);
        if (answer is null) throw new InvalidOperationException("Inventory cost decision answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.IsReplay || answer.OriginalCorrelationId is not null || answer.Code is not null
            || answer.Field is not null || answer.ExpectedVersion is not null || answer.CurrentVersion is not null
            || answer.Resource is not { } detail || detail.Proposal is not { Preparation: { } preparation } p
            || p.CompanyId != context.CompanyId || p.ProposalId != itemId || p.Kind != CostKind
            || !AccountingInventoryCostServiceV1.ValidInventoryCostDecisionDetail(detail)
            || AccountingInventoryCostDecisionBindingV1.FromView(p, detail.PreparedRecordedAtUtc) is not { } item)
            throw new InvalidOperationException("Inventory cost decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, p.Version.ToString(CultureInfo.InvariantCulture), p.Status == "Prepared" && detail.SourceCancelled == false)
        {
            InventoryCostProof = new(context.CompanyId, itemId, (int)CostKind, p.EventId, p.InvoiceId, p.OfficialInvoiceNumber,
                preparation.PeriodId, preparation.PreparedBy, detail.PreparedRecordedAtUtc, p.Status, detail.SourceCancelled!.Value,
                detail.SourceFactsDigest, detail.PreviewDigest),
        };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Inventory cost decision authority unavailable.");
        }
    }
}
