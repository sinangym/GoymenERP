using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// FNS-D73: the Purchasing-owned answer to <see cref="IPurchaseOrderExchangeRateReadPortV1"/> over Purchasing's own company-filtered
/// order header. A purchase order records no exchange rate yet (where the order's rate is specified is NEEDS-SINAN), so a recorded,
/// approved order always answers <see cref="PurchaseOrderExchangeRateReasonsV1.RateNotSpecified"/> with its version and recorded
/// currency: the consumer's conversion stays "hesaplanamadı" and no other rate stands in. Untracked; creates nothing (no attempt,
/// audit or version effect). A Draft is <see cref="PurchaseOrderExchangeRateReasonsV1.OrderNotApproved"/>; a Cancelled order keeps
/// the receipts made against it, so it is answered like an approved one. A stored header that cannot be answered (a company
/// mismatch, a version below one, an unknown status, a half currency snapshot) is
/// <see cref="PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable"/>.
/// </summary>
internal sealed class PurchaseOrderExchangeRateReadPort(PurchasingDbContext dbContext, ICompanyContext companyContext)
    : IPurchaseOrderExchangeRateReadPortV1
{
    public async Task<PurchaseOrderExchangeRateAnswerV1> GetAsync(Guid companyId, Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyContext.CompanyId != companyId)
        {
            // Deny-first: without the caller's resolved company, or for another company, there is no tenant filter to read under.
            throw new InvalidOperationException("Alış siparişi kuru yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (purchaseOrderId == Guid.Empty)
        {
            return PurchaseOrderExchangeRateAnswerV1.NotFound(companyId, purchaseOrderId);
        }

        var order = await dbContext.Orders.AsNoTracking()
            .Where(row => row.Id == purchaseOrderId)
            .Select(row => new OrderFacts(row.CompanyId, row.Status, row.Version, row.CurrencyId, row.CurrencyCode))
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            // Unknown, or another company's order hidden by the tenant filter: NotFound, never 403.
            return PurchaseOrderExchangeRateAnswerV1.NotFound(companyId, purchaseOrderId);
        }

        if (order.CompanyId != companyId || order.Version < 1 || !Enum.IsDefined(typeof(PurchaseOrderStatus), order.Status))
        {
            return Stored(companyId, purchaseOrderId);
        }

        var hasCurrencyId = order.CurrencyId is { } currencyId && currencyId != Guid.Empty;
        var hasCurrencyCode = !string.IsNullOrWhiteSpace(order.CurrencyCode);
        if (hasCurrencyId != hasCurrencyCode || (order.CurrencyId is not null && !hasCurrencyId)
            || (order.CurrencyCode is not null && !hasCurrencyCode))
        {
            // The order's money snapshot is all or nothing (A2a-1); a half snapshot is never echoed.
            return Stored(companyId, purchaseOrderId);
        }

        var reason = (PurchaseOrderStatus)order.Status == PurchaseOrderStatus.Draft
            ? PurchaseOrderExchangeRateReasonsV1.OrderNotApproved
            : PurchaseOrderExchangeRateReasonsV1.RateNotSpecified;

        // FNS-D73: the order records no rate (NEEDS-SINAN: where and by whom it is specified); no rate table, bulletin, order-day
        // or receipt-day rate is substituted, so the answer is never Rated until the order itself records one.
        return PurchaseOrderExchangeRateAnswerV1.Unavailable(companyId, purchaseOrderId, order.Version, order.CurrencyId,
            order.CurrencyCode, reason);
    }

    private static PurchaseOrderExchangeRateAnswerV1 Stored(Guid companyId, Guid purchaseOrderId) =>
        PurchaseOrderExchangeRateAnswerV1.Unavailable(companyId, purchaseOrderId, null, null, null,
            PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable);

    private sealed record OrderFacts(Guid CompanyId, int Status, long Version, Guid? CurrencyId, string? CurrencyCode);
}
