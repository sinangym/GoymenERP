using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// BS-1 (SAT-D295): central adapter from the Purchasing-owned <see cref="IPurchaseInvoiceCostRateReadPortV1"/> to Inventory's
/// <see cref="IInventoryPurchaseInvoiceCostRateSourceV1"/>. Purchasing keeps its rows, company filter and refusal; Inventory gets only
/// the receipt's order line, order currency and, once the supplier invoice is recorded, its official rate.
/// <para>
/// Fail-closed: any Purchasing failure other than the caller's own cancellation (a refused company context included), and any answer that does not
/// echo the asked company and receipt or does not hold together, reads as
/// <see cref="InventoryPurchaseInvoiceCostRateStatusV1.Unavailable"/> with <see cref="InventoryPurchaseInvoiceCostRateReasonsV1.Unreadable"/>,
/// never as a rate. NotFound stays NotFound. Reading writes nothing.
/// </para>
/// </summary>
public sealed class InventoryPurchaseInvoiceCostRateHost(IPurchaseInvoiceCostRateReadPortV1 purchasing)
    : IInventoryPurchaseInvoiceCostRateSourceV1
{
    public async Task<InventoryPurchaseInvoiceCostRateV1> ReadAsync(Guid companyId, Guid goodsReceiptLineId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId));
        }

        PurchaseInvoiceCostRateAnswerV1? answer;
        try
        {
            answer = await purchasing.GetAsync(companyId, goodsReceiptLineId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unreadable(companyId, goodsReceiptLineId);
        }

        try
        {
            return Map(companyId, goodsReceiptLineId, answer);
        }
        catch (ArgumentException)
        {
            // The owner's answer does not fit Inventory's factories (an empty identity, a rate not from the order currency).
            return Unreadable(companyId, goodsReceiptLineId);
        }
    }

    private static InventoryPurchaseInvoiceCostRateV1 Map(Guid companyId, Guid goodsReceiptLineId, PurchaseInvoiceCostRateAnswerV1? answer)
    {
        if (answer is null || answer.CompanyId != companyId || answer.GoodsReceiptLineId != goodsReceiptLineId)
        {
            return Unreadable(companyId, goodsReceiptLineId);
        }

        switch (answer.Outcome)
        {
            case PurchaseInvoiceCostRateOutcomeV1.NotFound:
                return InventoryPurchaseInvoiceCostRateV1.NotFound(companyId, goodsReceiptLineId);

            case PurchaseInvoiceCostRateOutcomeV1.Unavailable:
                if (answer.PurchaseOrderId is not { } unavailableOrder || answer.PurchaseOrderLineId is not { } unavailableLine
                    || answer.Rate is not null
                    || !string.Equals(answer.UnavailableReason, PurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded, StringComparison.Ordinal))
                {
                    return Unreadable(companyId, goodsReceiptLineId);
                }

                return InventoryPurchaseInvoiceCostRateV1.Unavailable(companyId, goodsReceiptLineId, unavailableOrder, unavailableLine,
                    answer.OrderCurrencyId, InventoryPurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded);

            case PurchaseInvoiceCostRateOutcomeV1.Rate:
                if (answer.PurchaseOrderId is not { } ratedOrder || answer.PurchaseOrderLineId is not { } ratedLine
                    || answer.OrderCurrencyId is not { } orderCurrency || answer.Rate is not { } rate || answer.UnavailableReason is not null)
                {
                    return Unreadable(companyId, goodsReceiptLineId);
                }

                return InventoryPurchaseInvoiceCostRateV1.Rated(companyId, goodsReceiptLineId, ratedOrder, ratedLine, orderCurrency, rate);

            default:
                return Unreadable(companyId, goodsReceiptLineId);
        }
    }

    private static InventoryPurchaseInvoiceCostRateV1 Unreadable(Guid companyId, Guid goodsReceiptLineId) =>
        InventoryPurchaseInvoiceCostRateV1.Unavailable(companyId, goodsReceiptLineId, null, null, null,
            InventoryPurchaseInvoiceCostRateReasonsV1.Unreadable);
}
