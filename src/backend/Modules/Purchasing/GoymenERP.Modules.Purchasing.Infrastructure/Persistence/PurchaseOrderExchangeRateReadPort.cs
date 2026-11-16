using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// FNS-D73 / ALC-SC33 (FNS-D79/D80): the Purchasing-owned answer to <see cref="IPurchaseOrderExchangeRateReadPortV1"/> over
/// Purchasing's own company-filtered order header and its append-only rate history. An approved order answers
/// <see cref="PurchaseOrderExchangeRateAnswerV1.Rated"/> with the rate it was approved with — the latest recorded rate, locked at
/// approval (FNS-D80) — from the order's currency to the rate's target (the functional currency); an approved order without a
/// recorded rate (no currency, or the functional currency) answers <see cref="PurchaseOrderExchangeRateReasonsV1.RateNotSpecified"/>
/// and no other rate stands in. Untracked; creates nothing (no attempt, audit or version effect). A Draft is
/// <see cref="PurchaseOrderExchangeRateReasonsV1.OrderNotApproved"/> (its rate may still change); a Cancelled order keeps the
/// receipts made against it, so it is answered like an approved one. A stored header or rate that cannot be answered (a company
/// mismatch, a version below one, an unknown status, a half currency snapshot, a rate on an order without a currency, a rate
/// recorded after the order's version or between the same currencies) is <see cref="PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable"/>.
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

        if ((PurchaseOrderStatus)order.Status == PurchaseOrderStatus.Draft)
        {
            // A Draft's rate may still change (FNS-D79): it is not the order's specified rate until approval locks it.
            return PurchaseOrderExchangeRateAnswerV1.Unavailable(companyId, purchaseOrderId, order.Version, order.CurrencyId,
                order.CurrencyCode, PurchaseOrderExchangeRateReasonsV1.OrderNotApproved);
        }

        // FNS-D80: the rate is locked at approval, so the latest recorded rate is the one the order was approved with.
        var rate = await dbContext.ExchangeRateChanges.AsNoTracking()
            .Where(row => row.OrderId == purchaseOrderId)
            .OrderByDescending(row => row.OrderVersionAfter)
            .Select(row => new RateFacts(row.CompanyId, row.OrderVersionAfter, row.Rate, row.TargetCurrencyId))
            .FirstOrDefaultAsync(cancellationToken);
        if (rate is null)
        {
            // FNS-D73: the order records no rate (no currency, or the functional currency); no rate table, bulletin, order-day or
            // receipt-day rate is substituted.
            return PurchaseOrderExchangeRateAnswerV1.Unavailable(companyId, purchaseOrderId, order.Version, order.CurrencyId,
                order.CurrencyCode, PurchaseOrderExchangeRateReasonsV1.RateNotSpecified);
        }

        if (rate.CompanyId != companyId || !hasCurrencyId || rate.OrderVersionAfter < 1 || rate.OrderVersionAfter > order.Version)
        {
            return Stored(companyId, purchaseOrderId);
        }

        ExchangeRate exchangeRate;
        try
        {
            exchangeRate = ExchangeRate.Of(order.CurrencyId!.Value, rate.TargetCurrencyId, rate.Rate);
        }
        catch (ArgumentException)
        {
            // An empty target, the order's own currency or a non-positive number: never echoed as a rate.
            return Stored(companyId, purchaseOrderId);
        }

        return PurchaseOrderExchangeRateAnswerV1.Rated(companyId, purchaseOrderId, order.Version, order.CurrencyId!.Value,
            order.CurrencyCode!, exchangeRate);
    }

    private static PurchaseOrderExchangeRateAnswerV1 Stored(Guid companyId, Guid purchaseOrderId) =>
        PurchaseOrderExchangeRateAnswerV1.Unavailable(companyId, purchaseOrderId, null, null, null,
            PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable);

    private sealed record OrderFacts(Guid CompanyId, int Status, long Version, Guid? CurrencyId, string? CurrencyCode);

    private sealed record RateFacts(Guid CompanyId, long OrderVersionAfter, decimal Rate, Guid TargetCurrencyId);
}
