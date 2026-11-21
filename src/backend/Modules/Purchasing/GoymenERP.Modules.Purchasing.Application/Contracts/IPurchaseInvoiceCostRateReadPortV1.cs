using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// BS-1 (SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR, F-011) with SAT-D295: the Purchasing-owned read port Inventory's average cost
/// asks for the exchange rate of ONE goods receipt line — the official rate on the supplier invoice that bills it. The goods receipt
/// line is the purchase acceptance share (<c>purchasing.PurchaseAcceptanceShares.AcceptanceId</c>), the same identity Inventory's
/// goods receipt movement carries as its source. BS-1: the TL cost of a foreign-currency receipt is the invoice's official rate;
/// until that invoice is recorded the answer is <see cref="PurchaseInvoiceCostRateOutcomeV1.Unavailable"/> — there is NO
/// provisional rate (no order-day, receipt-day or bulletin rate stands in for it) and no correction movement follows later.
/// <para>
/// Purchasing reads finalised incoming invoices and their receipt bindings. A fully covered receipt whose sources share one
/// official rate answers <see cref="PurchaseInvoiceCostRateOutcomeV1.Rate"/>. Missing, pending, incomplete or mixed-rate sources
/// answer Unavailable with an explicit reason; no weighted-rate policy is inferred. The answer carries the purchase order's
/// currency as a recorded fact (or null when the order has no money snapshot); Purchasing does NOT decide which currency is the
/// company's base currency and never answers "no rate needed" — that judgement is the consumer's. No price, amount or quantity
/// member exists: the foreign-currency value (SAT-D295, the purchase order line price) is read elsewhere.
/// </para>
/// <para>
/// The company is a required input and is validated server-side: it must equal the server-resolved company context, otherwise the
/// call is refused (<see cref="InvalidOperationException"/>) and nothing is read. Another company's receipt is NotFound, never 403.
/// The port carries no authority: the consumer measures its own permission. Reading here creates nothing (no attempt, audit or
/// version effect); a stored value that cannot be answered throws (a host adapter reads it as unreadable), never a guessed rate.
/// </para>
/// </summary>
public interface IPurchaseInvoiceCostRateReadPortV1
{
    /// <summary>
    /// The invoiced exchange rate of the goods receipt line <paramref name="goodsReceiptLineId"/> (a purchase acceptance id) of
    /// <paramref name="companyId"/>. An empty or unknown identity, or another company's receipt, is
    /// <see cref="PurchaseInvoiceCostRateOutcomeV1.NotFound"/>.
    /// </summary>
    Task<PurchaseInvoiceCostRateAnswerV1> GetAsync(Guid companyId, Guid goodsReceiptLineId, CancellationToken cancellationToken = default);
}

public enum PurchaseInvoiceCostRateOutcomeV1
{
    /// <summary>The supplier invoice billing the receipt is recorded; <see cref="PurchaseInvoiceCostRateAnswerV1.Rate"/> carries its official rate.</summary>
    Rate = 1,

    /// <summary>
    /// The receipt exists in the company but no invoiced rate can be answered (BS-1: until the invoice arrives the TL cost is
    /// Unavailable; no provisional rate). <see cref="PurchaseInvoiceCostRateAnswerV1.UnavailableReason"/> says why.
    /// </summary>
    Unavailable = 2,

    /// <summary>No such goods receipt line in the company (or an empty identity).</summary>
    NotFound = 3,
}

/// <summary>The fixed <see cref="PurchaseInvoiceCostRateAnswerV1.UnavailableReason"/> codes.</summary>
public static class PurchaseInvoiceCostRateReasonsV1
{
    /// <summary>No supplier invoice billing this receipt is recorded in Purchasing (BS-1: Unavailable until it arrives).</summary>
    public const string InvoiceNotRecorded = "purchase_invoice_not_recorded";

    public const string InvoiceNotFinalized = "purchase_invoice_not_finalized";
    public const string InvoiceCoverageIncomplete = "purchase_invoice_coverage_incomplete";
    public const string MultipleOfficialRates = "purchase_invoice_multiple_official_rates";
    public const string NoForeignCurrencyRate = "purchase_invoice_no_foreign_currency_rate";

    /// <summary>Every reason this contract version may answer.</summary>
    public static IReadOnlyList<string> All { get; } =
        [InvoiceNotRecorded, InvoiceNotFinalized, InvoiceCoverageIncomplete, MultipleOfficialRates, NoForeignCurrencyRate];
}

/// <summary>
/// The answer for one goods receipt line. The company and the asked identity are always echoed. Once found, the purchase order and
/// its line that the receipt counts on are named, with the order's currency as recorded (both null when the order carries no money
/// snapshot). <see cref="PurchaseInvoiceCostRateAnswerV1.Rate"/> is set only for <see cref="PurchaseInvoiceCostRateOutcomeV1.Rate"/>, from the order currency to
/// the invoice's target currency; <see cref="PurchaseInvoiceCostRateAnswerV1.UnavailableReason"/> only for <see cref="PurchaseInvoiceCostRateOutcomeV1.Unavailable"/>.
/// Built only through its factories, which refuse every other combination.
/// </summary>
public sealed record PurchaseInvoiceCostRateAnswerV1
{
    private PurchaseInvoiceCostRateAnswerV1(
        PurchaseInvoiceCostRateOutcomeV1 outcome,
        Guid companyId,
        Guid goodsReceiptLineId,
        Guid? purchaseOrderId,
        Guid? purchaseOrderLineId,
        Guid? orderCurrencyId,
        string? orderCurrencyCode,
        ExchangeRate? rate,
        string? unavailableReason)
    {
        Outcome = outcome;
        CompanyId = companyId;
        GoodsReceiptLineId = goodsReceiptLineId;
        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderLineId = purchaseOrderLineId;
        OrderCurrencyId = orderCurrencyId;
        OrderCurrencyCode = orderCurrencyCode;
        Rate = rate;
        UnavailableReason = unavailableReason;
    }

    public PurchaseInvoiceCostRateOutcomeV1 Outcome { get; }

    public Guid CompanyId { get; }

    public Guid GoodsReceiptLineId { get; }

    public Guid? PurchaseOrderId { get; }

    public Guid? PurchaseOrderLineId { get; }

    public Guid? OrderCurrencyId { get; }

    public string? OrderCurrencyCode { get; }

    public ExchangeRate? Rate { get; }

    public string? UnavailableReason { get; }

    /// <summary>No such goods receipt line in <paramref name="companyId"/>.</summary>
    public static PurchaseInvoiceCostRateAnswerV1 NotFound(Guid companyId, Guid goodsReceiptLineId)
    {
        RequireCompany(companyId);
        return new PurchaseInvoiceCostRateAnswerV1(PurchaseInvoiceCostRateOutcomeV1.NotFound, companyId, goodsReceiptLineId,
            null, null, null, null, null, null);
    }

    /// <summary>The receipt is recorded on the given order line, but no invoiced rate can be answered, for <paramref name="reason"/>.</summary>
    public static PurchaseInvoiceCostRateAnswerV1 Unavailable(Guid companyId, Guid goodsReceiptLineId, Guid purchaseOrderId,
        Guid purchaseOrderLineId, Guid? orderCurrencyId, string? orderCurrencyCode, string reason)
    {
        RequireReceipt(companyId, goodsReceiptLineId, purchaseOrderId, purchaseOrderLineId);
        RequireCurrency(orderCurrencyId, orderCurrencyCode);
        if (string.IsNullOrEmpty(reason) || !PurchaseInvoiceCostRateReasonsV1.All.Contains(reason, StringComparer.Ordinal))
        {
            throw new ArgumentException("Alış faturası kuru okunamama nedeni tanımsız.", nameof(reason));
        }

        return new PurchaseInvoiceCostRateAnswerV1(PurchaseInvoiceCostRateOutcomeV1.Unavailable, companyId, goodsReceiptLineId,
            purchaseOrderId, purchaseOrderLineId, orderCurrencyId, orderCurrencyCode, null, reason);
    }

    /// <summary>
    /// The supplier invoice's official <paramref name="rate"/> for the receipt: from the order's currency (which must be recorded and
    /// must be the rate's source) to the invoice's target currency.
    /// </summary>
    public static PurchaseInvoiceCostRateAnswerV1 Rated(Guid companyId, Guid goodsReceiptLineId, Guid purchaseOrderId,
        Guid purchaseOrderLineId, Guid orderCurrencyId, string orderCurrencyCode, ExchangeRate rate)
    {
        RequireReceipt(companyId, goodsReceiptLineId, purchaseOrderId, purchaseOrderLineId);
        RequireCurrency(orderCurrencyId, orderCurrencyCode);
        if (orderCurrencyId == Guid.Empty || rate.SourceCurrencyId != orderCurrencyId || rate.TargetCurrencyId == Guid.Empty
            || rate.TargetCurrencyId == rate.SourceCurrencyId || rate.Rate <= 0m)
        {
            throw new ArgumentException("Alış faturası kuru sipariş para biriminden başka bir para birimine pozitif bir kur olmalıdır.",
                nameof(rate));
        }

        return new PurchaseInvoiceCostRateAnswerV1(PurchaseInvoiceCostRateOutcomeV1.Rate, companyId, goodsReceiptLineId,
            purchaseOrderId, purchaseOrderLineId, orderCurrencyId, orderCurrencyCode, rate, null);
    }

    private static void RequireCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId));
        }
    }

    private static void RequireReceipt(Guid companyId, Guid goodsReceiptLineId, Guid purchaseOrderId, Guid purchaseOrderLineId)
    {
        RequireCompany(companyId);
        if (goodsReceiptLineId == Guid.Empty || purchaseOrderId == Guid.Empty || purchaseOrderLineId == Guid.Empty)
        {
            throw new ArgumentException("Mal kabul satırı, alış siparişi ve sipariş satırı kimlikleri boş olamaz.", nameof(goodsReceiptLineId));
        }
    }

    private static void RequireCurrency(Guid? currencyId, string? currencyCode)
    {
        // The order's money snapshot is all or nothing (A2a-1): an identity without a code, or a code without an identity, is refused.
        var hasId = currencyId is { } id && id != Guid.Empty;
        var hasCode = !string.IsNullOrWhiteSpace(currencyCode);
        if (hasId != hasCode || (currencyId is { } empty && empty == Guid.Empty))
        {
            throw new ArgumentException("Sipariş para birimi kimliği ve kodu birlikte bulunmalıdır.", nameof(currencyId));
        }
    }
}
