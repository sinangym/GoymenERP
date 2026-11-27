using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class PaymentReversalProposalService : IPaymentReversalDecisionReadV1
{
    public async Task<ReadResult<PaymentReversalProposalView>> ReadDecisionAsync(FinanceContext context, Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        if (await Fence() is { } before) return before;
        var answer = await DetailAuthorizedAsync(context, proposalId, cancellationToken);
        if (await Fence() is { } after) return after;
        if (answer.Outcome != ReadOutcome.Found || answer.Resource is not { } view) return answer;
        if (view.Id != proposalId || view.PaymentId == Guid.Empty || string.IsNullOrWhiteSpace(view.ProposedBy)) return new(ReadOutcome.Unavailable, null);
        return view.ProposedBy == context.ActorId ? new(ReadOutcome.Denied, null) : answer;
        async Task<ReadResult<PaymentReversalProposalView>?> Fence()
        {
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            var refusal = await FenceAsync(context, FinanceAction.ApprovePaymentReversal, proposalId, cancellationToken);
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            return refusal is null ? null : new(refusal.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
    }
}
