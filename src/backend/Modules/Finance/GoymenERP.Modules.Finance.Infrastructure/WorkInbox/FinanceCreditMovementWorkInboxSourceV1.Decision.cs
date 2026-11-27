using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceCreditMovementWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is not IFinanceCreditMovementDecisionReadV1 reader) throw new InvalidOperationException("Credit movement decision owner unavailable.");
        var answer = await reader.ReadDecisionAsync(new(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId), itemId, Fence, token);
        await Fence(token);
        if (answer.Outcome == ReadOutcome.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Outcome == ReadOutcome.NotFound && answer.Resource is null) return null;
        if (answer.Outcome != ReadOutcome.Found || answer.Resource is not { } detail || detail.Movement is not { } view
            || view.CompanyId != context.CompanyId || view.Id != itemId || view.IsOwnProposal
            || !FinanceCreditMovementServiceV1.ValidDecisionMovement(view, detail.ProposedBy)
            || detail.SourceFactsDigest != FinanceCreditMovementServiceV1.SourceFactsDigest(view, detail.ProposedBy)
            || DecisionItem(detail) is not { } item) throw new InvalidOperationException("Credit movement decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, view.Version, view.Status == CreditMovementStatusV1.Proposed,
            CreditMovementProof: new(context.CompanyId, itemId, detail.ProposedBy, view.ProposedAtUtc, view.Status.ToString(),
                detail.SourceFactsDigest, FinanceCreditMovementServiceV1.DecisionPreviewDigest(detail)));
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refused = await fence.CheckAsync(context, ApproverPermission, ct);
            ct.ThrowIfCancellationRequested();
            if (refused?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refused is not null) throw new InvalidOperationException("Credit movement authority unavailable.");
        }
    }

    // Project into the exact existing inbox formatter; no amount formatting or rule calculation is duplicated.
    internal static WorkInboxItemV1? DecisionItem(FinanceCreditMovementDecisionDetailV1 detail)
    {
        var p = detail.Movement;
        var banks = new Dictionary<Guid, string>();
        if (p.Source.BankEffect is { } bank && detail.BankDisplayName is { } name) banks.Add(bank.BankAccountId, name);
        return Item(new FinanceCreditMovementRow { Id = p.Id, CompanyId = p.CompanyId, CreditAccountId = p.Source.CreditAccountId,
            CurrencyId = p.CurrencyId, Kind = (int)p.Source.Kind, BankAccountId = p.Source.BankEffect?.BankAccountId,
            BankAmountText = p.Source.BankEffect?.Amount, ReplacesOpeningMovementId = p.Source.ReplacesOpeningMovementId,
            SourceJson = "{}", SourceIdentityHash = "", MoneyProfileJson = "{}", ProposedBy = detail.ProposedBy, ProposedAtUtc = p.ProposedAtUtc }, banks);
    }
}
