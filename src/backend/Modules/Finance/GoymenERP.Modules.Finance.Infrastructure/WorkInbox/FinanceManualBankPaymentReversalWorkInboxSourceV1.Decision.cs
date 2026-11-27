using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceManualBankPaymentReversalWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        await Fence(token);
        if (owner is not IPaymentReversalDecisionPreviewReadV1 reader)
            throw new InvalidOperationException("Payment reversal decision owner is unavailable.");
        var result = await reader.ReadDecisionPreviewAsync(new(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId), itemId, Fence, token);
        await Fence(token);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new UnauthorizedAccessException();
        if (result.Outcome == ReadOutcome.NotFound && result.Resource is null) return null;
        if (result.Outcome != ReadOutcome.Found || result.Resource is not { } view || view.CompanyId != context.CompanyId
            || view.Proposal.Id != itemId || !PaymentReversalProposalService.ValidProposalView(view.Proposal)
            || view.RecordedFactsDigest is not { Length: 64 }
            || !view.RecordedFactsDigest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            || DecisionItem(view) is not { } item)
            throw new InvalidOperationException("Payment reversal decision facts are unavailable.");
        return new(context.CompanyId, item, view.Proposal.Version, view.Proposal.Status == ReversalProposalStatus.Pending,
            ManualBankPaymentReversalProof: new(view.CompanyId, view.Proposal.Id, view.Proposal.PaymentId,
                view.Proposal.ProposedBy, view.Proposal.ProposedAtUtc, view.Proposal.Status.ToString(),
                view.RecordedFactsDigest, PaymentReversalProposalService.DecisionPreviewDigest(view)));
        async Task Fence(CancellationToken ct)
        {
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refused = await fence.CheckAsync(context, ApproverPermission, ct);
            if (refused?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refused is not null) throw new InvalidOperationException("Payment reversal decision authority is unavailable.");
        }
    }

    // Reuse the pending Inbox's native Item formatter, captions, rules and four references even for terminal proposals.
    private static WorkInboxItemV1? DecisionItem(PaymentReversalDecisionPreviewV1 view) => Item(
        new PaymentReversalProposalRow { Id = view.Proposal.Id, PaymentId = view.Proposal.PaymentId,
            CompanyId = view.CompanyId, ProposedBy = view.Proposal.ProposedBy, ProposedAtUtc = view.Proposal.ProposedAtUtc },
        new Lookup(new Dictionary<Guid, ManualBankPaymentRow>
        {
            [view.Proposal.PaymentId] = new() { Id = view.Proposal.PaymentId, CompanyId = view.CompanyId,
                DocumentNumber = view.PaymentDocumentNumber, PartyId = view.PartyId, TreasuryAccountId = view.TreasuryAccountId,
                CurrencyId = view.CurrencyId, AmountText = view.Amount },
        }, new Dictionary<Guid, string> { [view.TreasuryAccountId] = view.TreasuryAccountLabel }));

    // Exact entire rule/reference binding, using the existing owner formatter, without putting money into private Work proof.
    internal static bool MatchesDecisionItem(WorkInboxItemV1 item, PaymentReversalDecisionPreviewV1 view) =>
        DecisionItem(view) is { } expected && item.Module == expected.Module && item.Kind == expected.Kind
        && item.ItemId == expected.ItemId && item.DocumentLabel == expected.DocumentLabel
        && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.PreparedBy == expected.PreparedBy && item.Link == expected.Link
        && item.Scope is null && item.ExcludedDeciders is null or { Count: 0 }
        && item.Rules.SequenceEqual(expected.Rules) && item.References.SequenceEqual(expected.References);
}
