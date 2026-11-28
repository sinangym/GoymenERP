using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

internal sealed partial class SalesQuoteConversionRequestWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(cancellationToken);
        if (itemId == Guid.Empty) return null;
        if (owner is null) throw new InvalidOperationException("Teklif dönüşüm talebi karar okuma sahibi kayıtlı değil.");
        var answer = await owner.ReadQuoteConversionDecisionAsync(itemId, Fence, cancellationToken); await Fence(cancellationToken);
        if (answer is { State: SalesQuoteDecisionReadStateV1.Denied, Detail: null }) throw new UnauthorizedAccessException();
        if (answer is { State: SalesQuoteDecisionReadStateV1.NotFound, Detail: null }) return null;
        if (answer is not { State: SalesQuoteDecisionReadStateV1.Found, Detail: { } detail }
            || detail.RequestId != itemId || detail.Quote?.Quote is not { } quote || quote.CompanyId != context.CompanyId
            || SalesQuoteConversionDecisionBindingV1.Item(detail) is not { } item)
            throw new InvalidOperationException("Teklif dönüşüm talebi karar olguları doğrulanamadı.");
        // Queue membership is actual Pending. A derived Approved or Stale request may still support native rejection.
        return new(context.CompanyId, item, quote.Version, detail.Request.Status == SalesApprovalRequestApiV1.StatusPending)
        { SalesQuoteConversionProof = SalesQuoteConversionDecisionBindingV1.Proof(detail) };

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!await DecisionAllowedAsync(context, token)) throw new UnauthorizedAccessException();
            token.ThrowIfCancellationRequested();
        }
    }
}
