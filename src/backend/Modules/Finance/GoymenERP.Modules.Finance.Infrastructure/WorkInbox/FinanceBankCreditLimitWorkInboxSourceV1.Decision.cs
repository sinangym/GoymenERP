using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceBankCreditLimitWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is not IFinanceBankCreditLimitDecisionReadV1 reader) throw new InvalidOperationException("Bank credit limit decision owner unavailable.");
        var answer = await reader.ReadDecisionAsync(new(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId), itemId, Fence, token);
        await Fence(token);
        if (answer.Outcome == ReadOutcome.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Outcome == ReadOutcome.NotFound && answer.Resource is null) return null;
        if (answer.Outcome != ReadOutcome.Found || answer.Resource is not { } detail || detail.Limit is not { } view
            || view.CompanyId != context.CompanyId || view.Id != itemId || view.IsOwnProposal
            || !FinanceCreditLimitServiceV1.ValidDecisionLimit(view, detail.ProposedBy)
            || detail.SourceFactsDigest != FinanceCreditLimitServiceV1.SourceFactsDigest(view, detail.ProposedBy)
            || DecisionItem(detail) is not { } item) throw new InvalidOperationException("Bank credit limit decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, view.Version, view.Status == CreditAccountStatus.Proposed,
            BankCreditLimitProof: new(context.CompanyId, itemId, detail.ProposedBy, view.ProposedAtUtc, view.Status.ToString(),
                detail.SourceFactsDigest, FinanceCreditLimitServiceV1.DecisionPreviewDigest(detail)));
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refused = await fence.CheckAsync(context, ApproverPermission, ct);
            ct.ThrowIfCancellationRequested();
            if (refused?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refused is not null) throw new InvalidOperationException("Bank credit limit authority unavailable.");
        }
    }

    // Project into the exact existing inbox formatter; no amount formatting or rule calculation is duplicated.
    internal static WorkInboxItemV1? DecisionItem(FinanceBankCreditLimitDecisionDetailV1 detail)
    {
        var p = detail.Limit;
        return Item(new FinanceCreditLimitRow { Id = p.Id, CompanyId = p.CompanyId, CreditAccountId = p.Source.CreditAccountId,
            LenderPartyId = p.LenderPartyId, LenderCompanyPartyAccountId = p.LenderCompanyPartyAccountId, CurrencyId = p.CurrencyId,
            AmountText = p.Source.Amount, SourceJson = "{}", ProposedBy = detail.ProposedBy, ProposedAtUtc = p.ProposedAtUtc }, true);
    }
}
