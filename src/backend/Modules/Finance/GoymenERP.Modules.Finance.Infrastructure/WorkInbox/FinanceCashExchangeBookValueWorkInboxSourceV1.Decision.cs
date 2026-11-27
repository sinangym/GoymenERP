using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceCashExchangeBookValueWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is not ICashExchangeBookValueDecisionReadV1 reader) throw new InvalidOperationException("Book-value decision owner unavailable.");
        var answer = await reader.ReadDecisionAsync(new(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId), itemId, Fence, token);
        await Fence(token);
        if (answer is null) throw new InvalidOperationException("Book-value decision owner returned no result.");
        if (answer.Outcome == ReadOutcome.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Outcome == ReadOutcome.NotFound && answer.Resource is null) return null;
        if (answer.Outcome != ReadOutcome.Found || answer.Resource is not { } detail || detail.DecisionProof is not { } p
            || p.CompanyId != context.CompanyId || p.ProposalId != itemId || CashExchangeBookValueServiceV1.DecisionItem(detail) is not { } item)
            throw new InvalidOperationException("Book-value decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, p.Version, p.Status == nameof(CashExchangeBookValueStatusV1.Pending),
            CashExchangeBookValueProof: new(p.CompanyId, p.ProposalId, p.TransferId, p.PreparedBy, p.PreparedAtUtc, p.Status,
                p.Version, p.TransferSourceHash, p.ProposalFactsDigest, p.PreviewDigest));
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refused = await fence.CheckAsync(context, ApproverPermission, ct); ct.ThrowIfCancellationRequested();
            if (refused?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refused is not null) throw new InvalidOperationException("Book-value authority unavailable.");
        }
    }
}
