namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- MOD-01 / API-06: the read-only customer risk check of an order ---------------------------------------------------------
//
// DEC-FIN-007 (KILITLI, K0): "Brüt risk = açık alacak + faturalanmamış sevkiyat + sevk edilmemiş onaylı sipariş kalanı;
// aşamalar arasında çift sayım yapılmaz." DEC-FIN-009 (KILITLI, K1): "Risk kontrolü siparişte uyarı/onay, sevkiyatta yeniden
// kesin kontrol yapar." This read only EXPOSES the check; the warning-or-approval at order time (API-07) and the hard check at
// shipment belong to their callers. Every part says where it comes from and whether its owner answered: a part whose owner
// cannot answer makes the result "Unavailable" (fail-closed), never "Within".

/// <summary>The stable wire names of a figure's <c>state</c>.</summary>
public static class SalesDraftRiskFigureStatesV1
{
    public const string Available = "Available";

    /// <summary>The owner module or record does not exist on main yet (invoices S5, credit-limit and collateral masters).</summary>
    public const string NoSource = "NoSource";

    /// <summary>The owner exists but is not composed, failed or answered a malformed value: no number.</summary>
    public const string Unavailable = "Unavailable";
}

/// <summary>The stable wire names of a figure's <c>kind</c>.</summary>
public static class SalesDraftRiskFigureKindsV1
{
    public const string OpenReceivables = "OpenReceivables";
    public const string ShippedNotInvoiced = "ShippedNotInvoiced";
    public const string ApprovedUnshippedRemainder = "ApprovedUnshippedRemainder";
    public const string CreditLimit = "CreditLimit";
    public const string Collateral = "Collateral";
}

/// <summary>The stable source names of the parts Sales computes itself or cannot reach.</summary>
public static class SalesDraftRiskSourcesV1
{
    /// <summary>The customer's open approved orders in the Sales store, with the delivery owner's shipped totals (AT-024 port).</summary>
    public const string ApprovedOrderRemainders = "sales.approved-order-remainders";

    /// <summary>The invoicing owner (S5): it does not exist on main, so "shipped but not invoiced" has no source.</summary>
    public const string Invoicing = "sales.invoicing";

    /// <summary>The customer-finance owner of <see cref="ISalesPartyRiskReadPortV1"/>, when it is not composed or did not answer.</summary>
    public const string PartyRiskOwner = "party-risk-owner";
}

/// <summary>The stable reason codes the Sales risk read adds to the calculator's own (<c>SalesRiskReasonCodes</c>).</summary>
public static class SalesDraftRiskReasonCodesV1
{
    /// <summary>No customer-finance owner registered <see cref="ISalesPartyRiskReadPortV1"/>, or it failed.</summary>
    public const string PartyRiskProviderUnavailable = "party_risk_provider_unavailable";

    /// <summary>The customer-finance owner answered another company or customer, or a malformed figure.</summary>
    public const string PartyRiskAnswerInvalid = "party_risk_answer_invalid";

    /// <summary>Invoices are S5: no owner can tell what was shipped but not invoiced.</summary>
    public const string InvoicingOwnerMissing = "invoicing_owner_missing";

    /// <summary>An open approved order exists but the delivery owner (AT-024) is not composed or failed.</summary>
    public const string DeliveryProviderUnavailable = "delivery_provider_unavailable";

    /// <summary>The delivery owner answered another company or order, an unknown line, or a total in another unit.</summary>
    public const string DeliveryAnswerInvalid = "delivery_answer_invalid";

    /// <summary>A line of an approved order has no unit price: an unpriced line is not free (never zero).</summary>
    public const string OrderLineUnpriced = "order_line_unpriced";

    /// <summary>A line is priced in another unit than it is ordered in; no unit conversion is applied here.</summary>
    public const string PriceUnitConversionUnavailable = "price_unit_conversion_unavailable";

    /// <summary>The approved quantities of an order awaiting re-approval cannot be taken exactly.</summary>
    public const string OrderBasisUnreadable = "order_basis_unreadable";

    /// <summary>A line amount does not fit an exact decimal, or a net price is negative: a malformed order.</summary>
    public const string OrderAmountInvalid = "order_amount_invalid";

    /// <summary>The customer has more open approved orders than one read takes: never a partial (smaller) sum.</summary>
    public const string OrderReadBoundExceeded = "order_read_bound_exceeded";
}

/// <summary>
/// MOD-01 / API-06: <c>GET /api/v1/sales/drafts/{id}/risk-check</c> under <c>sales.drafts.read</c>.
/// <list type="bullet">
/// <item><c>result</c>: <c>Within</c>, <c>Over</c> (the gross risk EXCEEDS the limit) or <c>Unavailable</c>, with every
/// <c>reasons</c> entry when it is not a plain answer.</item>
/// <item><c>currencyId</c>: the currency the check is made in — the order's; amounts in another currency are not converted.</item>
/// <item><c>grossRisk</c>: open receivables + shipped-not-invoiced + approved-unshipped remainder (DEC-FIN-007), present only when
/// all three answered in that currency and on one tax basis and the caller holds <c>sales.orders.price.read</c>.</item>
/// <item><c>components</c>: the three parts in that order; <c>creditLimit</c>; <c>collateral</c> — shown only, never part of the
/// gross risk (net-risk policy, OPEN-010 open).</item>
/// <item><c>amountDisclosure</c>: <c>Withheld</c> without the price read — every amount is then null; the states, sources,
/// reasons and the result stay.</item>
/// </list>
/// </summary>
public sealed record SalesOrderDraftRiskCheckResponse(
    string DraftVersion,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    string Result,
    IReadOnlyList<SalesDraftRiskReasonV1> Reasons,
    SalesPriceDisclosure AmountDisclosure,
    string? GrossRisk,
    IReadOnlyList<SalesDraftRiskFigureV1> Components,
    SalesDraftRiskFigureV1 CreditLimit,
    SalesDraftRiskFigureV1 Collateral);

/// <summary>Why the result is not a plain "Within" / "Over": the part it is about (or <c>Check</c>) and a stable code.</summary>
public sealed record SalesDraftRiskReasonV1(string Component, string Code);

/// <summary>
/// One part of the check: its kind, its owner's source name, its state, the tax basis of its amounts (<c>IncludingTax</c> /
/// <c>ExcludingTax</c>; null for a limit or when not answered), its amounts per currency (null when not available or withheld;
/// an empty list is a recorded zero) and the reason when it is not available.
/// </summary>
public sealed record SalesDraftRiskFigureV1(
    string Kind,
    string Source,
    string State,
    string? TaxBasis,
    IReadOnlyList<SalesDraftRiskAmountV1>? Amounts,
    string? ReasonCode);

/// <summary>One amount in one currency as exact invariant text (padded to the order's money scale when it is in that currency).</summary>
public sealed record SalesDraftRiskAmountV1(Guid CurrencyId, string Amount);
