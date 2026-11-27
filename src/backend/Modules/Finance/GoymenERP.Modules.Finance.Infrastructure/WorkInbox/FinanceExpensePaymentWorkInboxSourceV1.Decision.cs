using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceExpensePaymentWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is not IExpensePaymentDecisionReadV1 reader) throw new InvalidOperationException("Expense payment decision owner unavailable.");
        var answer = await reader.ReadDecisionAsync(new(context.CompanyId, context.UserId, context.CorrelationId, context.CausationId), itemId, Fence, token);
        await Fence(token);
        if (answer.Outcome == ReadOutcome.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Outcome == ReadOutcome.NotFound && answer.Resource is null) return null;
        if (answer.Outcome != ReadOutcome.Found || answer.Resource is not { } detail || detail.Payment is not { } view
            || view.CompanyId != context.CompanyId || view.Id != itemId || !ExpensePaymentService.ValidDecisionPayment(view)
            || detail.Revisers is null || detail.DraftFactsDigest != ExpensePaymentService.DraftFactsDigest(view)
            || DecisionItem(detail) is not { } item) throw new InvalidOperationException("Expense payment decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, view.Version, view.Status == ExpensePaymentStatus.Draft,
            ExpensePaymentProof: new(context.CompanyId, itemId, view.PreparedBy, view.PreparedAtUtc, view.Status.ToString(),
                detail.DraftFactsDigest, ExpensePaymentService.DecisionPreviewDigest(detail)));
        async Task Fence(CancellationToken ct)
        {
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refused = await fence.CheckAsync(context, ApproverPermission, ct);
            if (refused?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refused is not null) throw new InvalidOperationException("Expense payment authority unavailable.");
        }
    }

    internal static WorkInboxItemV1? DecisionItem(ExpensePaymentDecisionDetailV1 detail)
    {
        var p = detail.Payment; var prep = p.Preparation;
        return Item(new ExpensePaymentRow { Id = p.Id, CompanyId = p.CompanyId, SourceKind = (int)prep.SourceKind,
            TreasuryAccountId = prep.TreasuryAccountId, ExpenseItemId = prep.ExpenseItemId, CurrencyId = prep.Money.CurrencyId,
            AmountText = prep.Money.Amount, PreparedBy = p.PreparedBy, PreparedAtUtc = p.PreparedAtUtc, DocumentNumber = p.DocumentNumber?.Number },
            new Lookup(new Dictionary<Guid, string> { [prep.TreasuryAccountId] = detail.TreasuryAccountLabel },
                new Dictionary<Guid, string> { [prep.TreasuryAccountId] = detail.TreasuryAccountLabel },
                new Dictionary<Guid, IReadOnlyList<string>> { [p.Id] = detail.Revisers }));
    }
}
