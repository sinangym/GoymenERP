using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// FNS-D73 (SINAN_KARARLARI_2026-11-15_DALGA2_EK12): the Purchasing-owned read port a cost conversion asks for the exchange rate
/// SPECIFIED ON ONE PURCHASE ORDER. FNS-D73: in a foreign currency ↔ TL cost conversion the system never chooses a TCMB rate kind on
/// its own (buying/selling, banknote buying/selling); for a cost that comes from a purchase the rate specified on the source
/// purchase order applies. Where a supplier invoice rate exists for a receipt, SAT-D295/BS-1 stays as is (the receipt's cost rate
/// is the invoice's, <see cref="IPurchaseInvoiceCostRateReadPortV1"/>); this port does not replace it.
/// <para>
/// A purchase order records no exchange rate yet: it carries one currency (its Settings money profile, A2a-1) and nothing else
/// about conversion. Where on the order the rate is specified (a rate typed on the order header, by whom, when, and whether it may
/// change after approval) is not decided (<c>NEEDS-SINAN</c>), so today every recorded, approved order answers
/// <see cref="PurchaseOrderExchangeRateOutcomeV1.Unavailable"/> with <see cref="PurchaseOrderExchangeRateReasonsV1.RateNotSpecified"/>
/// and the consumer's conversion stays "hesaplanamadı" (fail-closed, FNS-D73: a conversion with no order rate is refused). The
/// slice that first records a rate on the order answers <see cref="PurchaseOrderExchangeRateOutcomeV1.Rate"/> through
/// <see cref="PurchaseOrderExchangeRateAnswerV1.Rated"/> without changing this contract. No rate table, bulletin, order-day or
/// receipt-day rate ever stands in for the order's rate here.
/// </para>
/// <para>
/// The answer carries the order's currency as a recorded fact (or null when the order was opened without a money snapshot);
/// Purchasing does NOT decide which currency is the company's base currency and never answers "no rate needed" — whether a
/// conversion is needed at all is the consumer's judgement with its own functional currency source.
/// </para>
/// <para>
/// The company is a required input and is validated server-side: it must equal the server-resolved company context, otherwise the
/// call is refused (<see cref="InvalidOperationException"/>) and nothing is read. Another company's order is NotFound, never 403.
/// The port carries no authority: the consumer measures its own permission. Reading creates nothing (no attempt, audit or version
/// effect). A stored order that cannot be answered is <see cref="PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable"/>, never
/// a guessed rate.
/// </para>
/// </summary>
public interface IPurchaseOrderExchangeRateReadPortV1
{
    /// <summary>
    /// The exchange rate specified on order <paramref name="purchaseOrderId"/> of <paramref name="companyId"/>. An empty or unknown
    /// identity, or another company's order, is <see cref="PurchaseOrderExchangeRateOutcomeV1.NotFound"/>.
    /// </summary>
    Task<PurchaseOrderExchangeRateAnswerV1> GetAsync(Guid companyId, Guid purchaseOrderId, CancellationToken cancellationToken = default);
}

public enum PurchaseOrderExchangeRateOutcomeV1
{
    /// <summary>The order specifies a rate; <see cref="PurchaseOrderExchangeRateAnswerV1.Rate"/> carries it.</summary>
    Rate = 1,

    /// <summary>
    /// The order exists in the company but no rate can be answered; <see cref="PurchaseOrderExchangeRateAnswerV1.UnavailableReason"/>
    /// says why (one of <see cref="PurchaseOrderExchangeRateReasonsV1.All"/>). The consumer's conversion is "hesaplanamadı".
    /// </summary>
    Unavailable = 2,

    /// <summary>No such order in the company (or an empty identity).</summary>
    NotFound = 3,
}

/// <summary>The fixed, machine-readable <see cref="PurchaseOrderExchangeRateAnswerV1.UnavailableReason"/> codes.</summary>
public static class PurchaseOrderExchangeRateReasonsV1
{
    /// <summary>The order is still a Draft: it is not an approved commitment and nothing is received against it.</summary>
    public const string OrderNotApproved = "purchase_order_not_approved";

    /// <summary>
    /// The order specifies no exchange rate (today: every order — the order records no rate yet; or an order opened without a
    /// currency). FNS-D73: the conversion is refused, never filled with another rate.
    /// </summary>
    public const string RateNotSpecified = "purchase_order_exchange_rate_not_specified";

    /// <summary>
    /// The stored order cannot be answered (a half money snapshot, a company mismatch, a version below one, an unknown status):
    /// fail-closed, never a guessed rate.
    /// </summary>
    public const string StoredOrderUnreadable = "purchase_order_unreadable";

    /// <summary>Every reason this contract version may answer.</summary>
    public static IReadOnlyList<string> All { get; } = [OrderNotApproved, RateNotSpecified, StoredOrderUnreadable];
}

/// <summary>
/// The answer for one purchase order. The company and the asked order are always echoed. <see cref="OrderVersion"/> is the order's
/// version when it was read; it is set for <see cref="PurchaseOrderExchangeRateOutcomeV1.Rate"/> and for the
/// <see cref="PurchaseOrderExchangeRateReasonsV1.OrderNotApproved"/> and <see cref="PurchaseOrderExchangeRateReasonsV1.RateNotSpecified"/>
/// answers, as are the order's recorded currency identity and code (both null when the order has no money snapshot).
/// <see cref="Rate"/> is set only for <see cref="PurchaseOrderExchangeRateOutcomeV1.Rate"/>, from the order currency to the rate's
/// target currency; <see cref="UnavailableReason"/> only for <see cref="PurchaseOrderExchangeRateOutcomeV1.Unavailable"/>. For
/// <see cref="PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable"/> nothing read is trusted, so no version or currency is
/// echoed. Built only through its factories, which refuse every other combination.
/// </summary>
public sealed record PurchaseOrderExchangeRateAnswerV1
{
    private PurchaseOrderExchangeRateAnswerV1(
        PurchaseOrderExchangeRateOutcomeV1 outcome,
        Guid companyId,
        Guid purchaseOrderId,
        long? orderVersion,
        Guid? orderCurrencyId,
        string? orderCurrencyCode,
        ExchangeRate? rate,
        string? unavailableReason)
    {
        Outcome = outcome;
        CompanyId = companyId;
        PurchaseOrderId = purchaseOrderId;
        OrderVersion = orderVersion;
        OrderCurrencyId = orderCurrencyId;
        OrderCurrencyCode = orderCurrencyCode;
        Rate = rate;
        UnavailableReason = unavailableReason;
    }

    public PurchaseOrderExchangeRateOutcomeV1 Outcome { get; }

    public Guid CompanyId { get; }

    public Guid PurchaseOrderId { get; }

    public long? OrderVersion { get; }

    public Guid? OrderCurrencyId { get; }

    public string? OrderCurrencyCode { get; }

    public ExchangeRate? Rate { get; }

    public string? UnavailableReason { get; }

    /// <summary>No such order in <paramref name="companyId"/>.</summary>
    public static PurchaseOrderExchangeRateAnswerV1 NotFound(Guid companyId, Guid purchaseOrderId)
    {
        RequireCompany(companyId);
        return new PurchaseOrderExchangeRateAnswerV1(PurchaseOrderExchangeRateOutcomeV1.NotFound, companyId, purchaseOrderId,
            null, null, null, null, null);
    }

    /// <summary>
    /// The order exists but no rate can be answered, for <paramref name="reason"/>. For
    /// <see cref="PurchaseOrderExchangeRateReasonsV1.OrderNotApproved"/> and <see cref="PurchaseOrderExchangeRateReasonsV1.RateNotSpecified"/>
    /// the order was read: <paramref name="orderVersion"/> is required (1 or more) and the currency is echoed as recorded (identity
    /// and code together, or neither). For <see cref="PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable"/> neither a version
    /// nor a currency is given.
    /// </summary>
    public static PurchaseOrderExchangeRateAnswerV1 Unavailable(Guid companyId, Guid purchaseOrderId, long? orderVersion,
        Guid? orderCurrencyId, string? orderCurrencyCode, string reason)
    {
        RequireOrder(companyId, purchaseOrderId);
        if (string.IsNullOrEmpty(reason) || !PurchaseOrderExchangeRateReasonsV1.All.Contains(reason, StringComparer.Ordinal))
        {
            throw new ArgumentException("Alış siparişi kuru okunamama nedeni tanımsız.", nameof(reason));
        }

        var stored = string.Equals(reason, PurchaseOrderExchangeRateReasonsV1.StoredOrderUnreadable, StringComparison.Ordinal);
        if (stored)
        {
            if (orderVersion is not null || orderCurrencyId is not null || orderCurrencyCode is not null)
            {
                throw new ArgumentException("Okunamayan siparişte sürüm veya para birimi verilmez.", nameof(orderVersion));
            }
        }
        else
        {
            if (orderVersion is not { } version || version < 1)
            {
                throw new ArgumentException("Sipariş sürümü okunan siparişte 1 veya daha büyük olarak verilir.", nameof(orderVersion));
            }

            RequireCurrency(orderCurrencyId, orderCurrencyCode);
        }

        return new PurchaseOrderExchangeRateAnswerV1(PurchaseOrderExchangeRateOutcomeV1.Unavailable, companyId, purchaseOrderId,
            orderVersion, orderCurrencyId, orderCurrencyCode, null, reason);
    }

    /// <summary>
    /// The <paramref name="rate"/> specified on the order, read at order version <paramref name="orderVersion"/>: from the order's
    /// currency (which must be recorded and must be the rate's source) to another currency.
    /// </summary>
    public static PurchaseOrderExchangeRateAnswerV1 Rated(Guid companyId, Guid purchaseOrderId, long orderVersion, Guid orderCurrencyId,
        string orderCurrencyCode, ExchangeRate rate)
    {
        RequireOrder(companyId, purchaseOrderId);
        if (orderVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(orderVersion), "Sipariş sürümü 1'den küçük olamaz.");
        }

        if (orderCurrencyId == Guid.Empty || string.IsNullOrWhiteSpace(orderCurrencyCode))
        {
            throw new ArgumentException("Kur yalnız para birimi kayıtlı siparişte verilir.", nameof(orderCurrencyId));
        }

        RequireCurrency(orderCurrencyId, orderCurrencyCode);
        if (rate.SourceCurrencyId != orderCurrencyId || rate.TargetCurrencyId == Guid.Empty
            || rate.TargetCurrencyId == rate.SourceCurrencyId || rate.Rate <= 0m)
        {
            throw new ArgumentException("Alış siparişi kuru sipariş para biriminden başka bir para birimine pozitif bir kur olmalıdır.",
                nameof(rate));
        }

        return new PurchaseOrderExchangeRateAnswerV1(PurchaseOrderExchangeRateOutcomeV1.Rate, companyId, purchaseOrderId, orderVersion,
            orderCurrencyId, orderCurrencyCode, rate, null);
    }

    private static void RequireCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId));
        }
    }

    private static void RequireOrder(Guid companyId, Guid purchaseOrderId)
    {
        RequireCompany(companyId);
        if (purchaseOrderId == Guid.Empty)
        {
            throw new ArgumentException("Alış siparişi kimliği boş olamaz.", nameof(purchaseOrderId));
        }
    }

    private static void RequireCurrency(Guid? currencyId, string? currencyCode)
    {
        // The order's money snapshot is all or nothing (A2a-1): an identity without a code, or a code without an identity, is refused.
        var hasId = currencyId is { } id && id != Guid.Empty;
        var hasCode = !string.IsNullOrWhiteSpace(currencyCode);
        if (hasId != hasCode || (currencyId is { } empty && empty == Guid.Empty) || (currencyCode is not null && !hasCode))
        {
            throw new ArgumentException("Sipariş para birimi kimliği ve kodu birlikte bulunmalıdır.", nameof(currencyId));
        }
    }
}
