using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceManualReceiptReversalWorkInboxSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        await Fence();
        if (reversalService is not IReceiptReversalDecisionReadV1 reader)
            throw new InvalidOperationException("Receipt reversal decision owner is unavailable.");
        var answer = await reader.ReadDecisionAsync(new(context.CompanyId, context.UserId, "work-receipt-reversal-preview", null), itemId,
            _ => Fence(), token);
        await Fence();
        if (answer.Outcome == ReadOutcome.NotFound) return null;
        if (answer.Outcome == ReadOutcome.Denied) throw new UnauthorizedAccessException();
        if (answer.Outcome != ReadOutcome.Found || answer.Resource is not { } detail || detail.CompanyId != context.CompanyId
            || detail.Proposal is null || detail.OriginalReceipt is null || detail.Proposal.Id != itemId || detail.Proposal.OriginalSourceId != detail.OriginalReceipt.SourceId
            || detail.Proposal.ProposedBy == context.UserId
            || FinanceWorkInboxRead.Amount(detail.OriginalReceipt.AmountText) is null)
            throw new InvalidOperationException("Receipt reversal decision read unavailable.");
        var proposal = detail.Proposal; var original = detail.OriginalReceipt;
        var item = ReversalProposalService.DecisionItem(detail)
            ?? throw new InvalidOperationException("Receipt reversal decision item is invalid.");
        return new(context.CompanyId, item, proposal.Version, proposal.Status == ReversalProposalStatus.Pending,
            ManualReceiptReversalProof: new(context.CompanyId, itemId, original.SourceId, proposal.ProposedBy,
                proposal.ProposedAtUtc, proposal.Status.ToString(), ReversalProposalService.RecordedFactsDigest(detail), ReversalProposalService.DecisionPreviewDigest(detail)));
        async Task Fence()
        {
            var refusal = await fence.CheckAsync(context, ApproverPermission, token);
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Receipt reversal decision read unavailable.");
        }
    }
}
