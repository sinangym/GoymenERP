using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// BS-1 (F-011): the Purchasing-owned answer to <see cref="IPurchaseInvoiceCostRateReadPortV1"/> over Purchasing's own
/// company-filtered rows. It finds the acceptance share (the goods receipt line), then the order it counts on and that order's
/// recorded currency. A fully matched and finalised supplier invoice supplies its official rate. Until the receipt's remaining
/// quantity (accepted minus undone/returned, ALS-D127) has a finalised source, or when the receipt has different official rates, the single-rate contract remains unavailable.
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
            .Select(row => new ShareFacts(row.CompanyId, row.OrderId, row.LineId, row.Quantity))
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

        // Capture the bindings and their latest immutable revisions in one statement. No cross-module table access,
        // write, transaction creation or borrowed-context state changes occur in this read port.
        var sources = await (from binding in dbContext.InvoiceReceiptBindings.AsNoTracking()
                             join revision in dbContext.IncomingInvoiceRevisions.AsNoTracking()
                                 on new { binding.CompanyId, binding.InvoiceId } equals new { revision.CompanyId, revision.InvoiceId }
                             where binding.AcceptanceId == goodsReceiptLineId
                                 && revision.Version == dbContext.IncomingInvoiceRevisions
                                     .Where(item => item.CompanyId == revision.CompanyId && item.InvoiceId == revision.InvoiceId)
                                     .Max(item => item.Version)
                             select new { Binding = binding, Revision = revision }).ToArrayAsync(cancellationToken);
        if (sources.Length == 0) return Unavailable(PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded);
        var rates = new List<(Guid Currency, Guid Target, decimal? Rate)>();
        decimal covered = 0m;
        var hasPending = false;
        foreach (var source in sources)
        {
            var invoice = (JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(source.Revision.SnapshotJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw Unreadable()).Restore();
            var binding = source.Binding;
            var match = invoice.Matches.SingleOrDefault(item => item.AcceptanceId == goodsReceiptLineId);
            if (invoice.CompanyId != companyId || invoice.Id != binding.InvoiceId || invoice.Version != source.Revision.Version
                || (int)invoice.Status != source.Revision.Status || binding.CompanyId != companyId
                || binding.OrderId != share.OrderId || binding.OrderLineId != share.LineId || binding.Quantity <= 0m
                || invoice.CurrencyId != order.CurrencyId || match is null || match.CompanyId != companyId
                || match.InvoiceLineId != binding.InvoiceLineId || match.Quantity != binding.Quantity
                || match.OrderId != share.OrderId || match.OrderLineId != share.LineId)
                throw Unreadable();
            covered = checked(covered + binding.Quantity);
            hasPending |= invoice.Status < PurchaseIncomingInvoiceStatus.FinancePending;
            rates.Add((invoice.CurrencyId, invoice.FunctionalCurrencyId, invoice.OfficialRate));
        }
        // ALS-D127 (BS-1 detail): the undone / supplier-returned part of the receipt is deducted; the rate is final once the
        // REMAINING quantity is wholly invoiced. Purchasing's own adjustment rows, same tenant filter.
        var undone = await dbContext.ReceiptAdjustments.AsNoTracking()
            .Where(row => row.AcceptanceId == goodsReceiptLineId)
            .SumAsync(row => (decimal?)row.UndoQuantity, cancellationToken) ?? 0m;
        if (CoverageRefusal(share.Quantity, undone, covered, hasPending) is { } refusal) return Unavailable(refusal);
        var distinctRates = rates.Distinct().ToArray();
        if (distinctRates.Length != 1) return Unavailable(PurchaseInvoiceCostRateReasonsV1.MultipleOfficialRates);
        var official = distinctRates[0];
        // The consumer decides whether the company currency needs conversion. No synthetic 1:1 exchange rate.
        if (official.Rate is not { } rate || official.Currency == official.Target)
            return Unavailable(PurchaseInvoiceCostRateReasonsV1.NoForeignCurrencyRate);
        return PurchaseInvoiceCostRateAnswerV1.Rated(companyId, goodsReceiptLineId, share.OrderId, share.LineId,
            official.Currency, order.CurrencyCode!, ExchangeRate.Of(official.Currency, official.Target, rate));

        PurchaseInvoiceCostRateAnswerV1 Unavailable(string reason) => PurchaseInvoiceCostRateAnswerV1.Unavailable(companyId,
            goodsReceiptLineId, share.OrderId, share.LineId, order.CurrencyId, order.CurrencyCode, reason);
    }

    /// <summary>
    /// ALS-D127: whether the invoices bound to a receipt fix its cost rate. The remaining quantity is the accepted quantity minus
    /// what was undone or returned; it must be wholly invoiced by finalised invoices. An invoice bound before a later return may
    /// cover more than remains (never more than was accepted). A wholly undone/returned receipt has nothing left to cost: no
    /// rate coverage (Unavailable, coverage incomplete). Null when the rate is final; an impossible stored state throws.
    /// </summary>
    internal static string? CoverageRefusal(decimal accepted, decimal undone, decimal covered, bool hasPending)
    {
        if (accepted <= 0m || undone < 0m || undone > accepted || covered < 0m || covered > accepted) throw Unreadable();
        if (hasPending) return PurchaseInvoiceCostRateReasonsV1.InvoiceNotFinalized;
        var remaining = accepted - undone;
        return remaining == 0m || covered < remaining ? PurchaseInvoiceCostRateReasonsV1.InvoiceCoverageIncomplete : null;
    }

    private static InvalidOperationException Unreadable() => new InvalidOperationException("Kayıtlı mal kabul satırının alış kuru okunamadı.");

    private sealed record ShareFacts(Guid CompanyId, Guid OrderId, Guid LineId, decimal Quantity);

    private sealed record OrderCurrencyFacts(Guid CompanyId, Guid? CurrencyId, string? CurrencyCode);
}
