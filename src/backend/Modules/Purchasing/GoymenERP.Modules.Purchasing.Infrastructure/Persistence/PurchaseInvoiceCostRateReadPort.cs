using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// BS-1 (F-011): the Purchasing-owned answer to <see cref="IPurchaseInvoiceCostRateReadPortV1"/> over Purchasing's own
/// company-filtered rows. It finds the acceptance share (the goods receipt line), then the order it counts on and that order's
/// recorded currency. No supplier invoice is recorded in Purchasing yet, so a found receipt is always
/// <see cref="PurchaseInvoiceCostRateOutcomeV1.Unavailable"/> with <see cref="PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded"/>:
/// BS-1 allows no provisional rate, so neither the order's day nor the receipt's day stands in for the invoice. Untracked; creates
/// nothing (no attempt, audit or version effect). A stored row that cannot be answered (a share without its order, a share or order
/// outside the asked company, a half money snapshot) throws, never a guessed answer.
/// </summary>
internal sealed class PurchaseInvoiceCostRateReadPort(PurchasingDbContext dbContext, ICompanyContext companyContext)
    : IPurchaseInvoiceCostRateReadPortV1
{
    public async Task<PurchaseInvoiceCostRateAnswerV1> GetAsync(Guid companyId, Guid goodsReceiptLineId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || companyContext.CompanyId != companyId)
        {
            // Deny-first: without the caller's resolved company, or for another company, there is no tenant filter to read under.
            throw new InvalidOperationException("Alış faturası kuru yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (goodsReceiptLineId == Guid.Empty)
        {
            return PurchaseInvoiceCostRateAnswerV1.NotFound(companyId, goodsReceiptLineId);
        }

        var share = await dbContext.Acceptances.AsNoTracking()
            .Where(row => row.AcceptanceId == goodsReceiptLineId)
            .Select(row => new ShareFacts(row.CompanyId, row.OrderId, row.LineId))
            .SingleOrDefaultAsync(cancellationToken);
        if (share is null)
        {
            // Unknown, or another company's share hidden by the tenant filter: NotFound, never 403.
            return PurchaseInvoiceCostRateAnswerV1.NotFound(companyId, goodsReceiptLineId);
        }

        if (share.CompanyId != companyId || share.OrderId == Guid.Empty || share.LineId == Guid.Empty)
        {
            throw Unreadable();
        }

        var order = await dbContext.Orders.AsNoTracking()
            .Where(row => row.Id == share.OrderId)
            .Select(row => new OrderCurrencyFacts(row.CompanyId, row.CurrencyId, row.CurrencyCode))
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null || order.CompanyId != companyId)
        {
            // The store writes a share only against its own company's order; a share without it cannot be answered.
            throw Unreadable();
        }

        var hasCurrencyId = order.CurrencyId is { } currencyId && currencyId != Guid.Empty;
        var hasCurrencyCode = !string.IsNullOrWhiteSpace(order.CurrencyCode);
        if (hasCurrencyId != hasCurrencyCode || (order.CurrencyId is { } emptyId && emptyId == Guid.Empty))
        {
            throw Unreadable();
        }

        // No supplier invoice is recorded yet: BS-1 answers Unavailable until it arrives (no provisional rate, no correction later).
        return PurchaseInvoiceCostRateAnswerV1.Unavailable(companyId, goodsReceiptLineId, share.OrderId, share.LineId, order.CurrencyId,
            order.CurrencyCode, PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded);
    }

    private static InvalidOperationException Unreadable() => new InvalidOperationException("Kayıtlı mal kabul satırının alış kuru okunamadı.");

    private sealed record ShareFacts(Guid CompanyId, Guid OrderId, Guid LineId);

    private sealed record OrderCurrencyFacts(Guid CompanyId, Guid? CurrencyId, string? CurrencyCode);
}
