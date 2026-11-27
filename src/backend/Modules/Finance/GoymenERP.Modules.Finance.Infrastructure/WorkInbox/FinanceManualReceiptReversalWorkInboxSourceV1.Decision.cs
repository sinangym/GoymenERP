using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceManualReceiptReversalWorkInboxSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        await Fence();
        if (reversalService is not IReceiptReversalDecisionReadV1 reader)
            throw new InvalidOperationException("Receipt reversal decision owner is unavailable.");
        var answer = await reader.ReadDecisionAsync(new(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId), itemId,
            _ => Fence(), token);
        await Fence();
        if (answer.Outcome == ReadOutcome.NotFound) return null;
        if (answer.Outcome == ReadOutcome.Denied) throw new UnauthorizedAccessException();
        if (answer.Outcome != ReadOutcome.Found || answer.Resource is not { } detail || detail.CompanyId != context.CompanyId
            || detail.Proposal is null || detail.OriginalReceipt is null || detail.Proposal.Id != itemId || detail.Proposal.OriginalSourceId != detail.OriginalReceipt.SourceId
            || FinanceWorkInboxRead.Amount(detail.OriginalReceipt.AmountText) is null)
            throw new InvalidOperationException("Receipt reversal decision read unavailable.");
        if (detail.Proposal.ProposedBy == context.UserId) throw new UnauthorizedAccessException();
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

    // Reuse exactly the native Inbox formatter, including its stored company bank display label.
    internal static WorkInboxItemV1? DecisionItem(ReceiptReversalDecisionDetailV1 detail) => Item(
        new ReversalProposalRow { Id = detail.Proposal.Id, OriginalSourceId = detail.OriginalReceipt.SourceId,
            CompanyId = detail.CompanyId, ProposedBy = detail.Proposal.ProposedBy, ProposedAtUtc = detail.Proposal.ProposedAtUtc },
        new Lookup(new Dictionary<Guid, ReceiptDraftRow>
        {
            [detail.OriginalReceipt.SourceId] = new() { Id = detail.OriginalReceipt.SourceId, CompanyId = detail.CompanyId,
                DocumentNumber = detail.OriginalReceipt.DocumentNumber, PartyId = detail.OriginalReceipt.PartyId,
                TreasuryAccountId = detail.OriginalReceipt.TreasuryAccountId, CurrencyId = detail.OriginalReceipt.CurrencyId,
                AmountText = detail.OriginalReceipt.AmountText },
        }, new Dictionary<Guid, string> { [detail.OriginalReceipt.TreasuryAccountId] = detail.TreasuryAccountLabel }));

}
