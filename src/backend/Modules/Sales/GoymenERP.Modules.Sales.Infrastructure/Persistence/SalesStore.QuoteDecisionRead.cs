using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore : ISalesQuoteDecisionReadStoreV1
{
    private Func<CancellationToken, Task>? quoteDecisionReadFence;

    public async Task<SalesQuoteDecisionSourceV1?> ReadQuoteConversionDecisionAsync(Guid requestId,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence);
        cancellationToken.ThrowIfCancellationRequested();
        var company = RequireResolvedCompany();
        if (quoteDecisionReadFence is not null || overDeliveryRequestDecisionReadFence is not null || riskExceptionDecisionReadFence is not null || contractDiscountDecisionReadFence is not null || customerReturnDecisionReadFence is not null || remainderDecisionReadFence is not null || ownedOverDeliveryRequestApproval is not null || ownedRemainderDecision is not null || ownedCustomerReturnDecision is not null || ownedContractDiscountDecision is not null || ownedRiskExceptionDecision is not null || ownedQuoteApproval is not null || ownedRequestApproval is not null || ownedPriceFloorApproval is not null || ownedDiscountApproval is not null) throw new InvalidOperationException("Selected read is not reentrant.");
        quoteDecisionReadFence = Fence;
        try
        {
            var source = await QuoteApprovalReadAsync(() => (from request in dbContext.ApprovalRequests.AsNoTracking()
                join quote in dbContext.Quotes.AsNoTracking() on request.SubjectId equals quote.Id
                where request.CompanyId == company && quote.CompanyId == company && request.Id == requestId
                    && request.Kind == (int)SalesApprovalRequestKind.QuoteExpiredConversion
                select new { Request = request, Quote = quote }).SingleOrDefaultAsync(cancellationToken), cancellationToken);
            if (source is null) return null;
            var quoteFacts = await RestoreQuoteAsync(source.Quote, cancellationToken);
            // Full child reads must belong to the same observed header/request revision, not mixed live facts.
            var unchanged = await QuoteApprovalReadAsync(() => (from request in dbContext.ApprovalRequests.AsNoTracking()
                join quote in dbContext.Quotes.AsNoTracking() on request.SubjectId equals quote.Id
                where request.CompanyId == company && quote.CompanyId == company && request.Id == requestId
                    && request.Version == source.Request.Version && quote.Id == source.Quote.Id && quote.Version == source.Quote.Version
                    && request.Kind == (int)SalesApprovalRequestKind.QuoteExpiredConversion
                select request.Id).AnyAsync(cancellationToken), cancellationToken);
            if (!unchanged) throw new InvalidOperationException("Selected quote/request changed during the read.");
            return new(SalesApprovalRequestRows.Restore(source.Request), quoteFacts);
        }
        finally { quoteDecisionReadFence = null; }

        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); RequireCompany(company);
            await freshFence(token);
            token.ThrowIfCancellationRequested(); RequireCompany(company);
        }
    }
}
