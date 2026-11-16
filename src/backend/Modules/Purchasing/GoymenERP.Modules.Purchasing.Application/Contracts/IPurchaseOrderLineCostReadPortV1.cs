using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// SAT-D295 (1)–(2) with BS-1 (SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET, SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR):
/// the Purchasing-owned read port Inventory's moving-average cost reads the PRICE INPUT from — the recorded price of ONE purchase
/// order line, in the order's own currency. SAT-D295 (1): the price input of the moving-average purchase cost is the Purchasing order
/// line. The exchange rate is NOT here: BS-1 takes the TL value from the supplier invoice's official rate, which
/// <see cref="IPurchaseInvoiceCostRateReadPortV1"/> answers (Unavailable until the invoice is recorded; no provisional rate). This port
/// never converts, never picks a rate and never decides which currency is the company's base currency — the answer carries the order
/// currency as a recorded fact and the consumer judges it with its own functional currency source.
/// <para>
/// The line is named by its purchase order and its line identity — the pair the goods receipt line (the acceptance share) counts
/// on, answered by <see cref="IPurchaseInvoiceCostRateReadPortV1"/> as <c>PurchaseOrderId</c>/<c>PurchaseOrderLineId</c>. A line
/// identity is unique only inside its order (<c>UX_PurchaseOrderLines_LineId</c>), so the order is part of the key. A technical line
/// carries no price of its own: its quantity is priced on its general line (A2a-1), so a technical line answers its general line's
/// price and names that line as <see cref="PurchaseOrderLineCostPriceV1.PricedLineId"/>.
/// </para>
/// <para>
/// Every price fact is answered as recorded and nothing is chosen: the unit price, the price unit (and whether it is the kilogram),
/// the discount and the maturity surcharge (vade farkı) as stored, and the shared calculator's exact discounted and net unit prices
/// (<see cref="CommercialLinePriceCalculator"/>, never rounded, SAT-D76). Which of them feeds the moving average is decided by
/// ALS-D104 (SINAN_KARARLARI_2026-11-15_DALGA2_EK12) and SAT-D386 (SINAN_KARARLARI_2026-11-16_DALGA4_EK15): the discounted unit
/// price — every discount the order records applied, VAT excluded (the order line price carries no tax) and the supplier's
/// maturity surcharge (vade farkı) excluded, which stays a separate expense (SAT-D263 (3)). The port answers it as
/// <see cref="PurchaseOrderLineCostPriceV1.MovingAverageUnitCost"/> so no consumer maps the fields on its own. A zero unit price is
/// a recorded price and is answered Known; how a zero-cost receipt counts is the consumer's rule (SAT-D297).
/// </para>
/// <para>
/// The company is a required input and is validated server-side: it must equal the server-resolved company context, otherwise the
/// call is refused (<see cref="InvalidOperationException"/>) and nothing is read. Another company's line is NotFound, never 403. The
/// port carries no authority: the consumer measures its own permission. Reading creates nothing (no attempt, audit or version
/// effect). A stored row that cannot be answered is <see cref="PurchaseOrderLineCostOutcomeV1.Unreadable"/> with
/// <see cref="PurchaseOrderLineCostReasonsV1.StoredLineUnreadable"/>, never a guessed price.
/// </para>
/// </summary>
public interface IPurchaseOrderLineCostReadPortV1
{
    /// <summary>
    /// The recorded price of line <paramref name="purchaseOrderLineId"/> of order <paramref name="purchaseOrderId"/> of
    /// <paramref name="companyId"/>. An empty or unknown identity, or another company's order, is
    /// <see cref="PurchaseOrderLineCostOutcomeV1.NotFound"/>.
    /// </summary>
    Task<PurchaseOrderLineCostAnswerV1> GetAsync(
        Guid companyId,
        Guid purchaseOrderId,
        Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);
}

public enum PurchaseOrderLineCostOutcomeV1
{
    /// <summary>The line's price is recorded; <see cref="PurchaseOrderLineCostAnswerV1.Price"/> carries it.</summary>
    Known = 1,

    /// <summary>No such order line in the company (or an empty identity).</summary>
    NotFound = 2,

    /// <summary>
    /// The line exists in the company but no price can be answered; <see cref="PurchaseOrderLineCostAnswerV1.UnreadableReason"/>
    /// says why (one of <see cref="PurchaseOrderLineCostReasonsV1.All"/>).
    /// </summary>
    Unreadable = 3,
}

/// <summary>The fixed, machine-readable <see cref="PurchaseOrderLineCostAnswerV1.UnreadableReason"/> codes.</summary>
public static class PurchaseOrderLineCostReasonsV1
{
    /// <summary>The order is still a Draft: its lines are not an approved commitment and nothing is received against them.</summary>
    public const string OrderNotApproved = "purchase_order_not_approved";

    /// <summary>The line (for a technical line: its general line) has no price yet — "fiyat bekliyor" (ALS-D18).</summary>
    public const string PricePending = "purchase_order_line_price_pending";

    /// <summary>
    /// The stored order or line cannot be answered (a half money snapshot, a price above the snapshot's price scale, a technical
    /// line without its general line, a company mismatch, an unknown kind or status): fail-closed, never a guessed price.
    /// </summary>
    public const string StoredLineUnreadable = "purchase_order_line_unreadable";

    /// <summary>Every reason this contract version may answer.</summary>
    public static IReadOnlyList<string> All { get; } = [OrderNotApproved, PricePending, StoredLineUnreadable];
}

/// <summary>
/// The answer for one purchase order line. The company and the asked order and line are always echoed.
/// <see cref="OrderVersion"/> is the order's version when the order was read (the line has no version of its own: its price is
/// written once with the line, A2a-1, and every change to the order bumps this version); it is set for Known and for the
/// <see cref="PurchaseOrderLineCostReasonsV1.OrderNotApproved"/> and <see cref="PurchaseOrderLineCostReasonsV1.PricePending"/>
/// answers. <see cref="Price"/> is set only for <see cref="PurchaseOrderLineCostOutcomeV1.Known"/>;
/// <see cref="UnreadableReason"/> only for <see cref="PurchaseOrderLineCostOutcomeV1.Unreadable"/>. Built only through its
/// factories, which refuse every other combination.
/// </summary>
public sealed record PurchaseOrderLineCostAnswerV1
{
    private PurchaseOrderLineCostAnswerV1(
        PurchaseOrderLineCostOutcomeV1 outcome,
        Guid companyId,
        Guid purchaseOrderId,
        Guid purchaseOrderLineId,
        long? orderVersion,
        PurchaseOrderLineCostPriceV1? price,
        string? unreadableReason)
    {
        Outcome = outcome;
        CompanyId = companyId;
        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderLineId = purchaseOrderLineId;
        OrderVersion = orderVersion;
        Price = price;
        UnreadableReason = unreadableReason;
    }

    public PurchaseOrderLineCostOutcomeV1 Outcome { get; }

    public Guid CompanyId { get; }

    public Guid PurchaseOrderId { get; }

    public Guid PurchaseOrderLineId { get; }

    public long? OrderVersion { get; }

    public PurchaseOrderLineCostPriceV1? Price { get; }

    public string? UnreadableReason { get; }

    /// <summary>No such order line in <paramref name="companyId"/>.</summary>
    public static PurchaseOrderLineCostAnswerV1 NotFound(Guid companyId, Guid purchaseOrderId, Guid purchaseOrderLineId)
    {
        RequireCompany(companyId);
        return new PurchaseOrderLineCostAnswerV1(PurchaseOrderLineCostOutcomeV1.NotFound, companyId, purchaseOrderId, purchaseOrderLineId,
            null, null, null);
    }

    /// <summary>
    /// The line exists but no price can be answered, for <paramref name="reason"/>. <paramref name="orderVersion"/> is required for
    /// <see cref="PurchaseOrderLineCostReasonsV1.OrderNotApproved"/> and <see cref="PurchaseOrderLineCostReasonsV1.PricePending"/>
    /// (the order was read) and absent for <see cref="PurchaseOrderLineCostReasonsV1.StoredLineUnreadable"/> (nothing read is
    /// trusted).
    /// </summary>
    public static PurchaseOrderLineCostAnswerV1 Unreadable(Guid companyId, Guid purchaseOrderId, Guid purchaseOrderLineId,
        long? orderVersion, string reason)
    {
        RequireLine(companyId, purchaseOrderId, purchaseOrderLineId);
        if (string.IsNullOrEmpty(reason) || !PurchaseOrderLineCostReasonsV1.All.Contains(reason, StringComparer.Ordinal))
        {
            throw new ArgumentException("Alış sipariş satırı fiyatı okunamama nedeni tanımsız.", nameof(reason));
        }

        var stored = string.Equals(reason, PurchaseOrderLineCostReasonsV1.StoredLineUnreadable, StringComparison.Ordinal);
        if (stored ? orderVersion is not null : orderVersion is not { } version || version < 1)
        {
            throw new ArgumentException("Sipariş sürümü yalnız okunan siparişte ve 1 veya daha büyük olarak verilir.", nameof(orderVersion));
        }

        return new PurchaseOrderLineCostAnswerV1(PurchaseOrderLineCostOutcomeV1.Unreadable, companyId, purchaseOrderId, purchaseOrderLineId,
            orderVersion, null, reason);
    }

    /// <summary>The line's recorded <paramref name="price"/>, read at order version <paramref name="orderVersion"/>.</summary>
    public static PurchaseOrderLineCostAnswerV1 Known(Guid companyId, Guid purchaseOrderId, Guid purchaseOrderLineId, long orderVersion,
        PurchaseOrderLineCostPriceV1 price)
    {
        RequireLine(companyId, purchaseOrderId, purchaseOrderLineId);
        ArgumentNullException.ThrowIfNull(price);
        if (orderVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(orderVersion), "Sipariş sürümü 1'den küçük olamaz.");
        }

        return new PurchaseOrderLineCostAnswerV1(PurchaseOrderLineCostOutcomeV1.Known, companyId, purchaseOrderId, purchaseOrderLineId,
            orderVersion, price, null);
    }

    private static void RequireCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId));
        }
    }

    private static void RequireLine(Guid companyId, Guid purchaseOrderId, Guid purchaseOrderLineId)
    {
        RequireCompany(companyId);
        if (purchaseOrderId == Guid.Empty || purchaseOrderLineId == Guid.Empty)
        {
            throw new ArgumentException("Alış siparişi ve sipariş satırı kimlikleri boş olamaz.", nameof(purchaseOrderLineId));
        }
    }
}

/// <summary>
/// The recorded price facts of one purchase order line, in the order's currency (<see cref="UnitPrice"/>'s
/// <see cref="Money.CurrencyId"/>, labelled by <see cref="CurrencyCode"/>; the code is the Settings owner's, never an identity):
/// <list type="bullet">
/// <item><see cref="PricedLineId"/>: the general line whose price applies (the asked line itself for a general line);</item>
/// <item><see cref="ProductId"/> and <see cref="LineUnitId"/>: the asked line's product and entry unit;</item>
/// <item><see cref="PriceUnitId"/>: the unit the price applies to — today always the line's entry unit (ALS-D58: no published
/// conversion), so a price unit other than <see cref="LineUnitId"/> is refused; <see cref="PriceUnitIsKilogram"/> says whether
/// that unit is the kilogram, as recorded with the line;</item>
/// <item><see cref="UnitPrice"/>: the unit price as typed, at the order's price scale (A2a-1c);</item>
/// <item>the discount (a percent of the unit price OR an amount, never both) and the maturity surcharge (a percent of the
/// discounted price OR an amount per unit, never both), as stored;</item>
/// <item><see cref="DiscountedUnitPrice"/> (unit price − discount) and <see cref="NetUnitPrice"/> ((unit price − discount) +
/// surcharge): exact, never rounded, from <see cref="CommercialLinePriceCalculator"/> — the one calculator the order's own amounts
/// use, never recomputed by the consumer;</item>
/// <item><see cref="MovingAverageUnitCost"/>: the unit price that feeds the moving-average purchase cost (ALS-D104, SAT-D386) —
/// exactly <see cref="DiscountedUnitPrice"/>: every discount the order records applied (a purchase order has only the line discount;
/// no document-level discount exists), VAT excluded and the maturity surcharge excluded. <see cref="NetUnitPrice"/> includes the
/// surcharge and therefore never feeds the moving average.</item>
/// </list>
/// Built only through <see cref="Of"/>, which refuses an inconsistent set.
/// </summary>
public sealed record PurchaseOrderLineCostPriceV1
{
    private PurchaseOrderLineCostPriceV1(
        Guid pricedLineId,
        Guid productId,
        Guid lineUnitId,
        Guid priceUnitId,
        bool priceUnitIsKilogram,
        Money unitPrice,
        string currencyCode,
        decimal? discountPercent,
        decimal? discountAmount,
        decimal? deferralPercent,
        decimal? deferralAmount,
        decimal discountedUnitPrice,
        decimal netUnitPrice,
        decimal movingAverageUnitCost)
    {
        PricedLineId = pricedLineId;
        ProductId = productId;
        LineUnitId = lineUnitId;
        PriceUnitId = priceUnitId;
        PriceUnitIsKilogram = priceUnitIsKilogram;
        UnitPrice = unitPrice;
        CurrencyCode = currencyCode;
        DiscountPercent = discountPercent;
        DiscountAmount = discountAmount;
        DeferralPercent = deferralPercent;
        DeferralAmount = deferralAmount;
        DiscountedUnitPrice = discountedUnitPrice;
        NetUnitPrice = netUnitPrice;
        MovingAverageUnitCost = movingAverageUnitCost;
    }

    public Guid PricedLineId { get; }

    public Guid ProductId { get; }

    public Guid LineUnitId { get; }

    public Guid PriceUnitId { get; }

    public bool PriceUnitIsKilogram { get; }

    public Money UnitPrice { get; }

    public string CurrencyCode { get; }

    public decimal? DiscountPercent { get; }

    public decimal? DiscountAmount { get; }

    public decimal? DeferralPercent { get; }

    public decimal? DeferralAmount { get; }

    public decimal DiscountedUnitPrice { get; }

    public decimal NetUnitPrice { get; }

    /// <summary>
    /// ALS-D104 / SAT-D386: the order currency unit price that enters the moving-average purchase cost — the discounted unit price
    /// (discounts applied, VAT excluded, maturity surcharge excluded). Exact and never rounded; equal to
    /// <see cref="DiscountedUnitPrice"/>.
    /// </summary>
    public decimal MovingAverageUnitCost { get; }

    /// <summary>
    /// The price facts of a line; the discounted and net unit prices are computed here by <see cref="CommercialLinePriceCalculator"/>.
    /// Refuses (<see cref="ArgumentException"/>, or the calculator's <see cref="OverflowException"/>) an empty identity, an empty or
    /// untrimmed currency code, a negative unit price, a price unit other than the line's unit, both discount kinds, both surcharge
    /// kinds, a surcharge on a zero price, and any value the calculator cannot compute.
    /// </summary>
    public static PurchaseOrderLineCostPriceV1 Of(
        Guid pricedLineId,
        Guid productId,
        Guid lineUnitId,
        Guid priceUnitId,
        bool priceUnitIsKilogram,
        Money unitPrice,
        string currencyCode,
        decimal? discountPercent,
        decimal? discountAmount,
        decimal? deferralPercent,
        decimal? deferralAmount)
    {
        if (pricedLineId == Guid.Empty || productId == Guid.Empty || lineUnitId == Guid.Empty || priceUnitId == Guid.Empty
            || unitPrice.CurrencyId == Guid.Empty)
        {
            throw new ArgumentException("Fiyatlı satır, ürün, birim, fiyat birimi ve para birimi kimlikleri boş olamaz.", nameof(pricedLineId));
        }

        if (string.IsNullOrWhiteSpace(currencyCode) || !string.Equals(currencyCode, currencyCode.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Para birimi kodu boş veya kenar boşluklu olamaz.", nameof(currencyCode));
        }

        if (priceUnitId != lineUnitId)
        {
            // ALS-D58: until a published conversion exists the price unit is the line's entry unit; nothing is converted here.
            throw new ArgumentException("Fiyat birimi satırın giriş birimi olmalıdır.", nameof(priceUnitId));
        }

        if (unitPrice.IsNegative)
        {
            throw new ArgumentException("Birim fiyat negatif olamaz.", nameof(unitPrice));
        }

        if (discountPercent is not null && discountAmount is not null)
        {
            throw new ArgumentException("İskonto ya yüzde ya tutar olarak verilir.", nameof(discountAmount));
        }

        if (deferralPercent is not null && deferralAmount is not null)
        {
            throw new ArgumentException("Vade farkı ya yüzde ya tutar olarak verilir.", nameof(deferralAmount));
        }

        if ((deferralPercent is not null || deferralAmount is not null) && unitPrice.IsZero)
        {
            throw new ArgumentException("Sıfır fiyatlı satırda vade farkı olamaz.", nameof(deferralAmount));
        }

        var discounted = CommercialLinePriceCalculator.NetUnitPrice(unitPrice.Amount, discountPercent, discountAmount);
        var net = CommercialLinePriceCalculator.NetUnitPrice(unitPrice.Amount, discountPercent, discountAmount, deferralPercent,
            deferralAmount);
        return new PurchaseOrderLineCostPriceV1(pricedLineId, productId, lineUnitId, priceUnitId, priceUnitIsKilogram, unitPrice,
            currencyCode, discountPercent, discountAmount, deferralPercent, deferralAmount, discounted, net,
            movingAverageUnitCost: discounted);
    }
}
