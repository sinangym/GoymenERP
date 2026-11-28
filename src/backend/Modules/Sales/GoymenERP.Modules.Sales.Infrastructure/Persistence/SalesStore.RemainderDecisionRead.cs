using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesRemainderDecisionReadStoreV1
{
    private Func<CancellationToken, Task>? remainderDecisionReadFence;
    public async Task<IReadOnlyList<SalesRemainderPendingV1>> ListPendingRemainderDecisionSourceAsync(int take,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take); ArgumentNullException.ThrowIfNull(freshFence); EnterRemainderRead(freshFence);
        try
        {
            var company = RequireResolvedCompany();
            return await RemainderReadAsync(() => (from change in dbContext.DraftRemainderChanges.AsNoTracking()
                join draft in dbContext.Drafts.AsNoTracking() on new { change.CompanyId, Id = change.DraftId } equals new { draft.CompanyId, draft.Id }
                where change.CompanyId == company && !dbContext.DraftRemainderDecisions.Any(d => d.CompanyId == company && d.DraftId == change.DraftId && d.ChangeNo == change.ChangeNo)
                orderby change.RequestedAtUtc, change.DraftId, change.ChangeNo
                select new SalesRemainderPendingV1(company, draft.Id, draft.DocumentNo, draft.CreatedBy, draft.Version, change.ChangeNo, change.Revision,
                    change.LineId, change.RequestedBy, change.RequestedAtUtc)).Take(take).ToArrayAsync(cancellationToken), cancellationToken);
        }
        finally { remainderDecisionReadFence = null; }
    }
    public async Task<SalesRemainderDecisionSourceV1?> ReadRemainderDecisionSourceAsync(Guid draftId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence); cancellationToken.ThrowIfCancellationRequested();
        EnterRemainderRead(freshFence);
        try
        {
            var company = RequireResolvedCompany();
            var row = await RemainderReadAsync(() => dbContext.Drafts.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == draftId, cancellationToken), cancellationToken);
            if (row is null) return null;
            var order = (await RemainderReadAsync(() => RestoreAsync([row], cancellationToken), cancellationToken)).Single();
            var latest = order.RemainderChanges.OrderByDescending(x => x.Request.ChangeNo).FirstOrDefault();
            if (latest is null) return null;
            var unchanged = await RemainderReadAsync(() => dbContext.Drafts.AsNoTracking().AnyAsync(x => x.CompanyId == company && x.Id == draftId && x.Version == row.Version, cancellationToken), cancellationToken);
            if (!unchanged) throw new InvalidOperationException("Satır kalanı seçimi okuma sırasında değişti.");
            return new(order, latest);
        }
        finally { remainderDecisionReadFence = null; }
    }
    private void EnterRemainderRead(Func<CancellationToken, Task> fence)
    {
        if (remainderDecisionReadFence is not null || ownedRemainderDecision is not null || ownedCustomerReturnDecision is not null || ownedRequestApproval is not null || ownedQuoteApproval is not null
            || ownedPriceFloorApproval is not null || ownedDiscountApproval is not null || ownedOverDeliveryRequestApproval is not null || ownedRiskExceptionDecision is not null
            || quoteDecisionReadFence is not null || overDeliveryRequestDecisionReadFence is not null || riskExceptionDecisionReadFence is not null || ownedContractDiscountDecision is not null)
            throw new InvalidOperationException("Satır kalanı okuması başka sahip işlemine giremez.");
        var company = RequireResolvedCompany();
        remainderDecisionReadFence = async token => { token.ThrowIfCancellationRequested(); RequireCompany(company); await fence(token); token.ThrowIfCancellationRequested(); RequireCompany(company); };
    }
    private async Task RemainderDecisionReadFenceAsync(CancellationToken token)
    {
        if (remainderDecisionReadFence is null && ownedRemainderDecision is null) return;
        token.ThrowIfCancellationRequested();
        if (remainderDecisionReadFence is { } read) await read(token);
        else await RemainderDecisionFenceAsync(token);
        token.ThrowIfCancellationRequested();
    }
    private async Task<T> RemainderReadAsync<T>(Func<Task<T>> read, CancellationToken token)
    { await RemainderDecisionReadFenceAsync(token); var result = await read(); await RemainderDecisionReadFenceAsync(token); return result; }
}
