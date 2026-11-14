using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

// ---- requests (strict wire shapes; PurchasingJson validates the shape, the service the meaning) ----

/// <summary>
/// POST /api/v1/purchasing/requests: the demand side (talep, §7.2), a Draft with lines. Every line names
/// the product (with the version the client saw, for the Product owner's answer), the Settings unit and the
/// exact quantity text (Ö2). <paramref name="EffectiveOn"/> is the business date the unit profile and the
/// product eligibility are read for.
/// </summary>
public sealed record CreatePurchaseRequestRequest(DateOnly EffectiveOn, IReadOnlyList<PurchaseRequestLineRequest> Lines);

/// <summary>
/// One request line. A1-2 (plan API-02): <paramref name="Source"/> is optional; absent or null is a manual line. It is never
/// written when null, so the content hash of a manual line — and every ADR-011 attempt recorded before A1-2 — is unchanged.
/// </summary>
public sealed record PurchaseRequestLineRequest(Guid LineId, Guid ProductId, string ExpectedProductVersion, Guid UnitId, string Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseRequestLineSourceRequest? Source = null);

/// <summary>
/// A1-2 (plan API-02; Üretim A-2, Satış S-A1): the line's source — <paramref name="Kind"/> is exactly <c>Stock</c>,
/// <c>Sales</c> or <c>Production</c> (a manual line sends no source; <c>Manual</c> is not accepted here, so a line has one
/// spelling for it), <paramref name="DocumentId"/> the source record (sales order, production plan, stock need) and
/// <paramref name="DocumentLineId"/> its line, both non-empty. The identities are opaque: Purchasing stores them as given and
/// does not read the source module (no port exists yet; validation is the producing module's follow-up).
/// </summary>
public sealed record PurchaseRequestLineSourceRequest(string Kind, Guid DocumentId, Guid DocumentLineId);

/// <summary>POST …/requests/{id}/submission: Draft → Submitted at the expected version.</summary>
public sealed record SubmitPurchaseRequestRequest(string ExpectedVersion);

/// <summary>A1-1 (ALS-D2): POST …/requests/{id}/approval — Submitted → Approved at the expected version, by someone other than its preparer and submitter.</summary>
public sealed record ApprovePurchaseRequestRequest(string ExpectedVersion);

/// <summary>
/// A1-1 (No.161 §1): POST …/requests/{id}/rejection — Submitted | Approved → Rejected at the expected version, with the
/// reason (required, at most 4000 characters, never cut), by someone other than its makers, refused while an APPROVED
/// order is bound to the request.
/// </summary>
public sealed record RejectPurchaseRequestRequest(string ExpectedVersion, string Reason);

/// <summary>
/// A1-1b (ALS-D46, ALS-D47): POST …/requests/{id}/withdrawal — Submitted → Withdrawn at the expected version, by the
/// request's preparer (creator) or submitter only, with the reason (required, at most 4000 characters, never cut), refused while an APPROVED order is
/// bound to the request (No.161 §1).
/// </summary>
public sealed record WithdrawPurchaseRequestRequest(string ExpectedVersion, string Reason);

/// <summary>
/// POST /api/v1/purchasing/orders: a commitment towards ONE supplier (the Cari party + its company account
/// with the Supplier role), a Draft with general lines. With <paramref name="SourceRequestId"/> the order is
/// opened for one APPROVED request of this company (A1-1, ALS-D2/ALS-D16) and every line MAY bind to one of that request's lines
/// (<see cref="PurchaseOrderLineRequest.SourceRequestLineId"/>) at the share this supplier takes
/// (multi-sourcing, Sinan #66 5738113623 §1); a bound line keeps the request line's product and unit.
/// Without a source request no line may bind.
/// <para>
/// A2a-1 (TEK-01, plan API-20, ALS-D5): <paramref name="CurrencyId"/> is the order's one currency (a Settings currency identity,
/// verified against the money profile of <c>purchasing.order.v1</c> on <paramref name="EffectiveOn"/>); it is required as soon as
/// one line carries a <see cref="PurchaseOrderLineRequest.Price"/> and optional otherwise (ALS-D18: an order may be opened, approved
/// and received without prices). Both are never written when null, so the content hash of an order without them — and every
/// ADR-011 attempt recorded before A2a-1 — is unchanged.
/// </para>
/// </summary>
public sealed record CreatePurchaseOrderRequest(
    Guid SupplierPartyId,
    Guid CompanyAccountId,
    Guid? SourceRequestId,
    DateOnly EffectiveOn,
    IReadOnlyList<PurchaseOrderLineRequest> Lines,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CurrencyId = null);

/// <summary>One general line; A2a-1: <paramref name="Price"/> is the optional manual price (absent: "fiyat bekliyor", ALS-D18).</summary>
public sealed record PurchaseOrderLineRequest(Guid LineId, Guid ProductId, string ExpectedProductVersion, Guid UnitId, string Quantity,
    Guid? SourceRequestLineId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PurchaseOrderLinePriceRequest? Price = null);

/// <summary>
/// A2a-1 (ALS-D5 "elle, iskontolu"; ALS-D51/D60): a line's manual price as exact decimal text — <paramref name="UnitPrice"/>
/// (zero or more — an explicit 0 is a price, apart from no price —, at most the money profile's decimals, never rounded), the <paramref name="PriceUnitId"/> it applies to (absent: the
/// line's entry unit; any other unit is refused until a unit conversion is published, ALS-D58) and at most one discount: a percent
/// of the unit price (0..100) or an amount (0..unit price). Absent fields are never written, so the hash is that of what was sent.
/// A2a-2 (ALS-D38, ALS-D75): at most one maturity surcharge (vade farkı) — <paramref name="DeferralPercent"/> of the discounted unit
/// price (0..100) or <paramref name="DeferralAmount"/> per unit — never on a 0 price; a deferred price typed directly as the unit
/// price is a plain price with neither (ALS-D76). Absent, they are never written: the hash of an earlier request is unchanged.
/// </summary>
public sealed record PurchaseOrderLinePriceRequest(
    string UnitPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PriceUnitId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DiscountPercent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DiscountAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeferralPercent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeferralAmount = null);

/// <summary>POST …/orders/{id}/approval: Draft → Approved at the expected version; the Σ-shares rule is measured in the same save.</summary>
public sealed record ApprovePurchaseOrderRequest(string ExpectedVersion);

/// <summary>
/// A1-3 (plan API-10; No.161 §2): POST …/orders/{id}/cancellation — Approved → Cancelled at the expected version: the WHOLE
/// not-yet-accepted remainder of every line is cancelled (even after a partial acceptance; no line- or quantity-level partial
/// cancel), with the reason (required, at most 4000 characters, never cut). Nothing physical or financial is recorded.
/// </summary>
public sealed record CancelPurchaseOrderRequest(string ExpectedVersion, string Reason);

/// <summary>
/// A3 API-30 (ALS-D33, ALS-D21 "sıra"): POST …/orders/{id}/lines/{lineId}/schedule — the line's delivery schedule (termin
/// dilimleri) at the expected version, replacing the previous one whole: 1..500 slices with distinct client ids, each a positive
/// quantity in the line's unit with its delivery date; their sum is exactly the line's ordered quantity. The previous schedule
/// stays as a revision of the earlier order version (append only).
/// </summary>
public sealed record SetPurchaseOrderLineScheduleRequest(string ExpectedVersion, IReadOnlyList<PurchaseOrderDeliverySliceRequest> Slices);

/// <summary>A3 API-30: one delivery slice — the client's slice id, the quantity as exact decimal text (the line's unit) and the date.</summary>
public sealed record PurchaseOrderDeliverySliceRequest(Guid SliceId, string Quantity, DateOnly DeliveryDate);

// ---- views --------------------------------------------------------------------------------------

/// <summary>
/// The request header. A1-2 (plan API-02): <see cref="SourceKinds"/> names the distinct sources of its lines in a fixed order
/// (<c>Manual</c>, <c>Stock</c>, <c>Sales</c>, <c>Production</c>), so the request pool shows where each need came from
/// (ALS-D16) without reading the lines; a request whose lines are all manual answers <c>["Manual"]</c>.
/// </summary>
public sealed record PurchaseRequestSummaryView(
    Guid Id, Guid CompanyId, string Status, string Version, DateOnly EffectiveOn,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int LineCount)
{
    public IReadOnlyList<string> SourceKinds { get; init; } = [];
}

/// <summary>A request line; A1-2: <see cref="Source"/> is the line's source as recorded, or null for a manual line.</summary>
public sealed record PurchaseRequestLineView(Guid LineId, int LineNo, Guid ProductId, string ProductVersion, Guid UnitId, string Quantity)
{
    public PurchaseRequestLineSourceView? Source { get; init; }
}

/// <summary>A1-2 (plan API-02): a line's recorded source — the kind (<c>Stock</c> | <c>Sales</c> | <c>Production</c>) and the opaque source identity.</summary>
public sealed record PurchaseRequestLineSourceView(string Kind, Guid DocumentId, Guid DocumentLineId);

/// <summary>A1-1: one decision on a request — who decided, when, and (a rejection or a withdrawal, A1-1b) why.</summary>
public sealed record PurchaseRequestDecisionView(string DecidedBy, DateTimeOffset DecidedAtUtc, string? Reason);

/// <summary>
/// The request with its lines; <see cref="Approval"/> and <see cref="Rejection"/> (A1-1) and <see cref="Withdrawal"/>
/// (A1-1b) are the recorded decisions, null until made. An approval stays visible after a later rejection.
/// <see cref="PreparedBy"/> and <see cref="SubmittedBy"/> (A1-1b) are the recorded makers — the only actors who may
/// withdraw (ALS-D47) — or null when not recorded (the submitter of a Draft, or of a submission recorded without its actor).
/// </summary>
public sealed record PurchaseRequestDetailView(PurchaseRequestSummaryView Request, IReadOnlyList<PurchaseRequestLineView> Lines)
{
    public PurchaseRequestDecisionView? Approval { get; init; }

    public PurchaseRequestDecisionView? Rejection { get; init; }

    public PurchaseRequestDecisionView? Withdrawal { get; init; }

    public string? PreparedBy { get; init; }

    public string? SubmittedBy { get; init; }
}

public sealed record PurchaseRequestResponse(PurchaseRequestDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record PurchaseRequestListResponse(IReadOnlyList<PurchaseRequestSummaryView> Items, string? NextCursor);

/// <param name="Status">An exact status name ("Draft" | "Submitted" | "Approved" | "Rejected" | "Withdrawn") to narrow the list to, or null.</param>
public sealed record PurchaseRequestListQuery(int PageSize = 50, string? Cursor = null, string? Status = null);

public sealed record PurchaseOrderSummaryView(
    Guid Id, Guid CompanyId, string Status, string Version, Guid SupplierPartyId, Guid CompanyAccountId, Guid? SourceRequestId,
    DateOnly EffectiveOn, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int LineCount)
{
    /// <summary>
    /// A2a-2 (ALS-D18, plan A2a-2 "listede fiyat bekliyor"): on a LIST item only, true while a general line has no price; never
    /// written when null — on a detail or a command answer, or when the caller lacks <c>purchasing.orders.price.read</c> (PRS-02).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PricePending { get; init; }
}

/// <summary>
/// Authorized list/detail projection. Unlike command responses, every returned order has an exact,
/// record-bound current Cari supplier display and never falls back to the raw Party id alone.
/// </summary>
public sealed record PurchaseOrderReadSummaryViewV1(
    Guid Id, Guid CompanyId, string Status, string Version, Guid SupplierPartyId, Guid CompanyAccountId, Guid? SourceRequestId,
    DateOnly EffectiveOn, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int LineCount,
    PurchasingSupplierDisplayV1 SupplierDisplay)
{
    /// <summary>A2a-2: "fiyat bekliyor" on a list item (see <see cref="PurchaseOrderSummaryView.PricePending"/>); never written when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PricePending { get; init; }
}

/// <summary>
/// The line's shares as exact text (§7.2, §13): ordered, moved into technical lines, committed (= ordered − technical), accepted,
/// remaining (= committed − accepted − cancelled, the expected supply; master INV-006). A1-3: <see cref="Cancelled"/> is the
/// unaccepted remainder cancelled with the order (No.161 §2), "0" until then.
/// </summary>
public sealed record PurchaseOrderLineView(
    Guid LineId, int LineNo, string Kind, Guid? ParentLineId, Guid? SourceRequestLineId,
    Guid ProductId, string ProductVersion, Guid UnitId,
    string Ordered, string Technical, string Committed, string Accepted, string Remaining)
{
    public string Cancelled { get; init; } = "0";

    /// <summary>
    /// A2a-1: the line's price, or null — "fiyat bekliyor" (ALS-D18) when the order's <c>PriceDisclosure</c> is <c>Visible</c>,
    /// withheld when it is <c>Withheld</c> (PRS-02). A technical line is never priced (its quantity is priced on its general line).
    /// </summary>
    public PurchaseOrderLinePriceView? Price { get; init; }

    /// <summary>
    /// A3 API-30 (ALS-D21): the line's current delivery schedule in slice order — empty until one is recorded; a technical line
    /// has none (its quantity is scheduled on its general line).
    /// </summary>
    public IReadOnlyList<PurchaseOrderDeliverySliceView> DeliverySchedule { get; init; } = [];
}

/// <summary>A3 API-30: one delivery slice of a line's current schedule, its quantity as exact text in the line's unit.</summary>
public sealed record PurchaseOrderDeliverySliceView(Guid SliceId, int SliceNo, string Quantity, DateOnly DeliveryDate);

/// <summary>
/// A2a-1 (TEK-01, API-20): a priced line's price as exact text in the order's currency — the unit price and its price unit, the
/// discount as given (a percent or an amount, or neither), the exact net unit price (never rounded, SAT-D76) and the line amount
/// (ordered quantity × net, rounded once half away from zero to the money profile's scale, SAT-D11), all from the one backend
/// calculator; the screen never recomputes them.
/// </summary>
public sealed record PurchaseOrderLinePriceView(
    string UnitPrice, Guid PriceUnitId, string? DiscountPercent, string? DiscountAmount, string NetUnitPrice, string LineAmount)
{
    /// <summary>A2a-2 (ALS-D38): the maturity surcharge percent of the discounted unit price as given, or null.</summary>
    public string? DeferralPercent { get; init; }

    /// <summary>A2a-2 (ALS-D38): the maturity surcharge amount per unit as given, or null.</summary>
    public string? DeferralAmount { get; init; }
}

/// <summary>A2a-2 (API-20): the states of the order totals (see <see cref="PurchaseOrderTotalsView"/>).</summary>
public static class PurchaseOrderTotalsStatesV1
{
    /// <summary>The value is computed.</summary>
    public const string Computed = "Computed";

    /// <summary>The document: a general line has no price ("fiyat bekliyor") or no amount, so no sum is shown.</summary>
    public const string Incomplete = "Incomplete";

    /// <summary>A line or a sum cannot be held exactly; nothing of it is shown (fail-closed).</summary>
    public const string AmountOutOfRange = "AmountOutOfRange";

    /// <summary>A line without a price.</summary>
    public const string Unpriced = "Unpriced";

    /// <summary>"hesaplanamıyor": no source answers (see the reason); never 0.</summary>
    public const string NotComputable = "NotComputable";

    /// <summary>No withholding applies (ALS-D78: the item type has no withholding).</summary>
    public const string NotApplied = "NotApplied";

    /// <summary>The base is missing (the line or a line of the document has no amount, or its VAT is not computed).</summary>
    public const string NotComputed = "NotComputed";

    /// <summary>PRS-02: the caller lacks <c>purchasing.orders.price.read</c>; every amount is null.</summary>
    public const string Withheld = "Withheld";
}

/// <summary>A2a-2: why a line's VAT or the order's withholding is <see cref="PurchaseOrderTotalsStatesV1.NotComputable"/>.</summary>
public static class PurchaseOrderTaxReasonsV1
{
    /// <summary>Legacy wire reason (N-20, before ALS-D78): no longer produced; kept so older clients keep parsing.</summary>
    public const string WithholdingUnconfirmed = "withholding_unconfirmed";

    /// <summary>The Settings line tax port is not composed or did not answer.</summary>
    public const string TaxSourceUnavailable = "settings_provider_unavailable";

    /// <summary>The Settings answer was not a usable VAT state.</summary>
    public const string TaxSourceInvalid = "tax_source_invalid";
}

/// <summary>
/// A2a-2 (API-20): one general line's totals as exact text in the order's currency. <c>State</c>: <c>Computed</c> | <c>Unpriced</c> |
/// <c>AmountOutOfRange</c>. <c>VatState</c>: <c>Computed</c> (with the percent, the VAT and the gross amount) | <c>NotComputable</c>
/// (with <c>VatReason</c>: a Settings reason such as a missing item type, or <see cref="PurchaseOrderTaxReasonsV1"/>) |
/// <c>NotComputed</c>. <c>WithholdingState</c> follows the item type's share (ALS-D78) whenever the VAT is computed, otherwise <c>NotComputed</c>.
/// </summary>
public sealed record PurchaseOrderLineTotalView(
    Guid LineId, int LineNo, string State, string? NetUnitPrice, string? NetAmount,
    string VatState, string? VatReason, string? VatPercent, string? VatAmount, string? GrossAmount, string WithholdingState);

/// <summary>
/// A2a-2 (API-20, plan TEK-01; ALS-D74, ALS-D77): the order's totals — the sum of the general lines' amounts (SAT-D11 "toplamlar
/// satırlardan"; a line's amount includes its maturity surcharge, and so does its VAT base — ALS-D77 "Vade farkı dahil hesaplansın"),
/// the VAT per line from the item type's default (Settings <c>ISettingsLineTaxResolverV1</c>, buyer situation
/// <c>Normal</c>, on the order's business date) through the shared <c>CommercialTaxTotalsCalculator</c>, and the gross amount.
/// Purchase withholding follows Sales S5 (ALS-D78): the item type's share from the rounded line VAT, no threshold gate; an unreadable
/// share is <c>NotComputable</c> and its amounts null. <see cref="PurchaseOrderTaxReasonsV1.WithholdingUnconfirmed"/> is kept for the wire only. A document with an unpriced line is <c>Incomplete</c> (no sum); a line whose VAT
/// cannot be computed makes the document VAT <c>NotComputable</c> with the count. PRS-02: without <c>purchasing.orders.price.read</c>
/// every state is <c>Withheld</c>, every amount null and the lines empty.
/// </summary>
public sealed record PurchaseOrderTotalsView(
    Guid OrderId, string Version, string PriceDisclosure, PurchaseOrderCurrencyView? Currency,
    string State, int UnpricedLineCount, string? NetAmount,
    string VatState, int VatNotComputableLineCount, string? VatAmount, string? GrossAmount,
    string WithholdingState, string? WithholdingReason, string? WithheldVatAmount, string? PayableAmount,
    IReadOnlyList<PurchaseOrderLineTotalView> Lines);

/// <summary>A2a-1: the order's currency as recorded at opening (the Settings money profile's currency, code and scale).</summary>
public sealed record PurchaseOrderCurrencyView(Guid CurrencyId, string CurrencyCode, int Scale);

/// <summary>
/// A2a-1 (PRS-02): whether the caller may see the order's prices. <c>Visible</c> needs <c>purchasing.orders.price.read</c>; without
/// it every price field is null beside <c>Withheld</c> — an answer, not an omission, so a withheld price is never mistaken for a
/// line without a price (the Sales <c>sales.orders.price.read</c> pattern).
/// </summary>
public static class PurchasingPriceDisclosureV1
{
    public const string Visible = "Visible";
    public const string Withheld = "Withheld";
}

/// <summary>A1-3 (No.161 §2): the recorded cancellation of an order's unaccepted remainder — who, when and why.</summary>
public sealed record PurchaseOrderCancellationView(string CancelledBy, DateTimeOffset CancelledAtUtc, string Reason);

/// <summary>
/// A1-3 (plan API-10): GET …/orders/{id}/cancel-impact — what a cancellation at <see cref="Version"/> would do, read under
/// <c>purchasing.orders.cancel</c> and changing nothing. <see cref="Cancellable"/> is true only for an APPROVED order with an
/// unaccepted remainder; otherwise <see cref="Blocker"/> is the refusal the command would answer
/// (<c>purchasing_order_not_cancellable</c> | <c>purchasing_order_nothing_to_cancel</c>). Every line shows its committed and
/// accepted shares and the remainder that would be cancelled (<see cref="PurchaseOrderCancelImpactLineView.ToCancel"/>);
/// <see cref="HasAcceptance"/> says a partial acceptance exists (it stays: No.161 §2). The effect is the future supply only —
/// no stock, receipt or money (<see cref="SourceRequestId"/> is the request whose binding is kept).
/// </summary>
public sealed record PurchaseOrderCancelImpactView(
    Guid OrderId, string Status, string Version, Guid? SourceRequestId, bool Cancellable, string? Blocker, bool HasAcceptance,
    IReadOnlyList<PurchaseOrderCancelImpactLineView> Lines);

/// <summary>A1-3: one line of the impact preview; quantities are exact text in the line's unit.</summary>
public sealed record PurchaseOrderCancelImpactLineView(
    Guid LineId, int LineNo, string Kind, Guid ProductId, Guid UnitId, string Committed, string Accepted, string ToCancel);

/// <summary>One immutable goods-acceptance share in the persisted order sequence; quantity is exact text in the order line's unit.</summary>
public sealed record PurchaseOrderAcceptanceHistoryView(
    Guid AcceptanceId, Guid LineId, string Quantity, DateTimeOffset RecordedAtUtc);

public sealed record PurchaseOrderDetailView(PurchaseOrderSummaryView Order, IReadOnlyList<PurchaseOrderLineView> Lines)
{
    public IReadOnlyList<PurchaseOrderAcceptanceHistoryView> Acceptances { get; init; } = [];

    /// <summary>A1-3 (No.161 §2): the recorded cancellation, null unless the order is Cancelled.</summary>
    public PurchaseOrderCancellationView? Cancellation { get; init; }

    /// <summary>A2a-1 (PRS-02): <c>Visible</c> | <c>Withheld</c> (see <see cref="PurchasingPriceDisclosureV1"/>); fail-closed default.</summary>
    public string PriceDisclosure { get; init; } = PurchasingPriceDisclosureV1.Withheld;

    /// <summary>A2a-1: the order's currency, or null for an order opened without one (not a price: shown under the order read).</summary>
    public PurchaseOrderCurrencyView? Currency { get; init; }

    /// <summary>
    /// A2a-1 (ALS-D18): true while a general line has no price ("fiyat bekliyor"); null when the prices are withheld (PRS-02).
    /// </summary>
    public bool? PricePending { get; init; }
}

public sealed record PurchaseOrderReadDetailViewV1(
    PurchaseOrderReadSummaryViewV1 Order,
    IReadOnlyList<PurchaseOrderLineView> Lines)
{
    public IReadOnlyList<PurchaseOrderAcceptanceHistoryView> Acceptances { get; init; } = [];

    /// <summary>A1-3 (No.161 §2): the recorded cancellation, null unless the order is Cancelled.</summary>
    public PurchaseOrderCancellationView? Cancellation { get; init; }

    /// <summary>A2a-1 (PRS-02): <c>Visible</c> | <c>Withheld</c> (see <see cref="PurchasingPriceDisclosureV1"/>); fail-closed default.</summary>
    public string PriceDisclosure { get; init; } = PurchasingPriceDisclosureV1.Withheld;

    /// <summary>A2a-1: the order's currency, or null for an order opened without one (not a price: shown under the order read).</summary>
    public PurchaseOrderCurrencyView? Currency { get; init; }

    /// <summary>
    /// A2a-1 (ALS-D18): true while a general line has no price ("fiyat bekliyor"); null when the prices are withheld (PRS-02).
    /// </summary>
    public bool? PricePending { get; init; }
}

public sealed record PurchaseOrderResponse(PurchaseOrderDetailView Detail, bool IsReplay, string OriginalCorrelationId);

public sealed record PurchaseOrderListResponse(IReadOnlyList<PurchaseOrderSummaryView> Items, string? NextCursor);

/// <summary>
/// A2a-1: GET …/references/currencies — the currencies whose Settings money profile answers the purchasing order operation
/// (<c>purchasing.order.v1</c>) for the context company, as the owner lists them (undefined order; none is a default). Being listed is
/// not validity: the order's create verifies the chosen currency again for its business date.
/// </summary>
public sealed record PurchasingCurrencyCandidatesResponseV1(IReadOnlyList<PurchasingCurrencyCandidateViewV1> Items);

/// <summary>A2a-1: one currency candidate — the owner's identity (the only reference sent back), its code and its display label.</summary>
public sealed record PurchasingCurrencyCandidateViewV1(Guid CurrencyId, string Code, string DisplayLabel);

/// <summary>
/// A2a-1 (TEK-01): the Purchasing operation key the Settings money port is asked with — the backend owner's technical mapping
/// (Business #254 5654539215), in the Sales (<c>sales.order-draft.v1</c>) and Finance pattern. The key chooses no currency, scale
/// or date. Until the Settings owner approves a money profile for it, the owner answers <c>settings_provider_unavailable</c> and a
/// priced order is refused (fail-closed). Sinan (#66 6704170096) approved the catalog entry as a narrow ALS-D58 exception; it lands
/// in A2a-1b (with the unit-price scale), not here.
/// </summary>
public static class PurchasingMoneyOperationsV1
{
    public const string OrderV1 = "purchasing.order.v1";
}

public sealed record PurchaseOrderReadListResponseV1(
    IReadOnlyList<PurchaseOrderReadSummaryViewV1> Items,
    string? NextCursor);

/// <param name="Status">An exact status name ("Draft" | "Approved" | "Cancelled") to narrow the list to, or null.</param>
/// <param name="SupplierPartyId">The exact supplier Party identity to narrow the list to, or null.</param>
public sealed record PurchaseOrderListQuery(
    int PageSize = 50,
    string? Cursor = null,
    string? Status = null,
    Guid? SupplierPartyId = null);

/// <summary>
/// A1-2 (plan API-02): the request-line source vocabulary on the wire. <see cref="Manual"/> appears only in a summary's
/// <see cref="PurchaseRequestSummaryView.SourceKinds"/>; a line says "manual" by carrying no source.
/// </summary>
public static class PurchaseRequestSourceKindsV1
{
    public const string Manual = "Manual";
    public const string Stock = "Stock";
    public const string Sales = "Sales";
    public const string Production = "Production";

    /// <summary>The exact (ordinal) name of a source kind; <see cref="Manual"/>, a different case or anything else is refused.</summary>
    public static bool TryParse(string? text, out PurchaseRequestSourceKind kind)
    {
        switch (text)
        {
            case Stock:
                kind = PurchaseRequestSourceKind.Stock;
                return true;
            case Sales:
                kind = PurchaseRequestSourceKind.Sales;
                return true;
            case Production:
                kind = PurchaseRequestSourceKind.Production;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static string Name(PurchaseRequestSourceKind kind) => kind switch
    {
        PurchaseRequestSourceKind.Stock => Stock,
        PurchaseRequestSourceKind.Sales => Sales,
        PurchaseRequestSourceKind.Production => Production,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The distinct kinds of the given line sources in the fixed order Manual, Stock, Sales, Production.</summary>
    public static IReadOnlyList<string> Distinct(IEnumerable<PurchaseRequestLineSource?> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var present = sources.Select(source => source is null ? 0 : (int)source.Kind).ToHashSet();
        return [.. new[] { (0, Manual), (1, Stock), (2, Sales), (3, Production) }.Where(item => present.Contains(item.Item1)).Select(item => item.Item2)];
    }
}

public sealed record PurchasingFieldError(string Field, string Code);

// ---- problems -----------------------------------------------------------------------------------

/// <summary>The problem codes the purchasing routes answer beside the domain's rule codes (PUR-4a contract).</summary>
public static class PurchasingProblemCodesV1
{
    /// <summary>400: the Idempotency-Key header is missing, repeated, blank, non-canonical, over 128 characters or malformed.</summary>
    public const string CommandMetadataInvalid = "command_metadata_invalid";

    public const string InvalidCursor = "invalid_cursor";

    /// <summary>409: the supplier account is not the party's, not this company's, not active, or has no Supplier role (the Sales/Logistics pattern).</summary>
    public const string AccountState = "account_state";

    /// <summary>
    /// 409 (fail-closed): the source request is not APPROVED (A1-1; field <c>sourceRequestId</c> = <c>not_approved</c>),
    /// unknown in this company, or a bound line does not match one of its lines; field errors say which. At an order's
    /// approval it is terminal (§7) when the source request is no longer APPROVED.
    /// </summary>
    public const string RequestState = "purchasing_request_state";

    /// <summary>
    /// 409, terminal (§7; A1-1): the request is not in the state the decision needs (approve: Submitted; reject: Submitted
    /// or Approved; withdraw, A1-1b: Submitted).
    /// </summary>
    public const string RequestDecisionState = "purchasing_request_decision_state";

    /// <summary>409, terminal (§7; ALS-D2): the actor prepared or submitted this request and cannot decide it (hazırlayan ≠ onaylayan).</summary>
    public const string RequestSameActor = "purchasing_request_same_actor";

    /// <summary>
    /// 409, terminal (§7; A1-1 fail-closed): the request's preparer or submitter is not recorded, so no one may decide it.
    /// A1-1b (ALS-D47): a withdrawal is refused with this code only when NEITHER maker is recorded; with one recorded, only
    /// that one may withdraw and anyone else is <see cref="RequestNotPreparer"/>.
    /// </summary>
    public const string RequestMakerUnknown = "purchasing_request_maker_unknown";

    /// <summary>409, terminal (§7; A2a-5a, ALS-D3): the actor prepared this order and cannot approve it (hazırlayan ≠ onaylayan).</summary>
    public const string OrderSameActor = "purchasing_order_same_actor";

    /// <summary>409, terminal (§7; A2a-5a fail-closed): the order's preparer is not recorded, so no one may approve it.</summary>
    public const string OrderMakerUnknown = "purchasing_order_maker_unknown";

    /// <summary>409, terminal (§7; No.161 §1): an APPROVED order is bound to the request; the order is cancelled first (rejection and A1-1b withdrawal).</summary>
    public const string RequestHasApprovedOrder = "purchasing_request_has_approved_order";

    /// <summary>422: the rejection reason is missing or blank.</summary>
    public const string RequestRejectionReasonRequired = "purchasing_request_rejection_reason_required";

    /// <summary>422: the rejection reason is longer than 4000 characters (refused, never cut).</summary>
    public const string RequestRejectionReasonTooLong = "purchasing_request_rejection_reason_too_long";

    /// <summary>
    /// 409, terminal (§7; A1-1b, ALS-D46/ALS-D47): the actor is neither the request's recorded preparer (creator) nor its
    /// recorded submitter; only they withdraw it.
    /// </summary>
    public const string RequestNotPreparer = "purchasing_request_not_preparer";

    /// <summary>422 (A1-1b): the withdrawal reason is missing or blank.</summary>
    public const string RequestWithdrawalReasonRequired = "purchasing_request_withdrawal_reason_required";

    /// <summary>422 (A1-1b): the withdrawal reason is longer than 4000 characters (refused, never cut).</summary>
    public const string RequestWithdrawalReasonTooLong = "purchasing_request_withdrawal_reason_too_long";

    /// <summary>409, terminal (§7): the expected version is stale.</summary>
    public const string VersionConflict = "version_conflict";

    /// <summary>
    /// 409, terminal (§7): the order is not in the state the command needs (an Approved order cannot be approved again; A1-3:
    /// only an Approved order's remainder is cancelled — field <c>status</c> = <c>purchasing_order_not_cancellable</c>).
    /// </summary>
    public const string OrderState = "purchasing_order_state";

    /// <summary>409, terminal (§7; A1-3 fail-closed): every line of the order is fully accepted; there is no remainder to cancel.</summary>
    public const string OrderNothingToCancel = "purchasing_order_nothing_to_cancel";

    /// <summary>422 (A1-3, No.161 §2): the cancellation reason is missing or blank.</summary>
    public const string OrderCancellationReasonRequired = "purchasing_order_cancellation_reason_required";

    /// <summary>422 (A1-3): the cancellation reason is longer than 4000 characters (refused, never cut).</summary>
    public const string OrderCancellationReasonTooLong = "purchasing_order_cancellation_reason_too_long";

    /// <summary>409, terminal (§7): the request is not in the state the command needs (a Submitted request cannot be submitted again).</summary>
    public const string RequestNotDraft = "purchasing_request_not_draft";

    /// <summary>409, terminal (§7; Sinan 5738113623 §1): approving this order would take the approved shares of a request line over its need.</summary>
    public const string RequestLineOverSourced = "purchasing_request_line_over_sourced";

    public const string SameKeyPayloadMismatch = "same_key_payload_mismatch";
    public const string ConcurrentChange = "concurrent_change";

    /// <summary>422: the unit is not an approved kilogram profile for this company on the date, or the quantity's scale exceeds it (Ö2).</summary>
    public const string UnsupportedUnit = "unsupported_unit";

    /// <summary>
    /// 422 (A2a-1): the currency has no Settings money profile for the purchasing order operation on the business date, or it is
    /// inactive or outside its window; field <c>currencyId</c>.
    /// </summary>
    public const string UnsupportedCurrency = "unsupported_currency";

    /// <summary>422: the Product owner refused this selection (not purchasable, lifecycle, unit binding); field errors carry the owner's codes.</summary>
    public const string UnsupportedProduct = "unsupported_product";

    /// <summary>503 (never on the wire): the Cari account owner cannot answer.</summary>
    public const string CounterpartyProviderUnavailable = "counterparty_provider_unavailable";

    /// <summary>503 (never on the wire): the Product owner has not published purchase eligibility, or cannot answer (fail-closed).</summary>
    public const string ProductEligibilityUnavailable = "product_eligibility_unavailable";

    /// <summary>503 (never on the wire): an owner answered a malformed value.</summary>
    public const string ReferenceProviderInvalid = "reference_provider_invalid";

    /// <summary>503 (never on the wire): a Purchasing permission name is not in the central catalogue yet.</summary>
    public const string PermissionUnregistered = "permission_unregistered";
}

public sealed class PurchasingServiceException(int status, string code,
    IReadOnlyList<PurchasingFieldError>? errors = null, bool isReplay = false, string? originalCorrelationId = null)
    : Exception($"{status} {code}")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyList<PurchasingFieldError> Errors { get; } = errors ?? [];
    public bool IsReplay { get; } = isReplay;
    public string? OriginalCorrelationId { get; } = originalCorrelationId;
}

/// <summary>The permission names the service measures; null = not bound → 503 setup problem, never a grant.</summary>
public sealed record PurchasingServiceBindings(
    string? CreatePermission = PurchasingPermissionManifestV1.OrdersCreate,
    string? ReadPermission = PurchasingPermissionManifestV1.OrdersRead,
    string? ApprovePermission = PurchasingPermissionManifestV1.OrdersApprove,
    string? GoodsReceiptPermission = PurchasingPermissionManifestV1.GoodsReceiptsRecord,
    string? RequestApprovePermission = PurchasingPermissionManifestV1.RequestsApprove,
    string? RequestWithdrawPermission = PurchasingPermissionManifestV1.RequestsWithdraw,
    string? OrderCancelPermission = PurchasingPermissionManifestV1.OrdersCancel,
    string? PriceReadPermission = PurchasingPermissionManifestV1.OrdersPriceRead,
    string? RevisePermission = PurchasingPermissionManifestV1.OrdersRevise);

/// <summary>Constructed by the server, never bound from the request body.</summary>
public sealed record PurchasingCommandMetadata(string IdempotencyKey, string CorrelationId, string? CausationId, DateTimeOffset RequestedAtUtc);

public static class PurchasingApiV1
{
    public const string RequestsPath = "/api/v1/purchasing/requests";
    public const string RequestDetailPath = "/api/v1/purchasing/requests/{id}";
    public const string RequestSubmissionPath = "/api/v1/purchasing/requests/{id}/submission";
    public const string RequestApprovalPath = "/api/v1/purchasing/requests/{id}/approval";
    public const string RequestRejectionPath = "/api/v1/purchasing/requests/{id}/rejection";
    public const string RequestWithdrawalPath = "/api/v1/purchasing/requests/{id}/withdrawal";
    public const string OrdersPath = "/api/v1/purchasing/orders";
    public const string OrderDetailPath = "/api/v1/purchasing/orders/{id}";
    public const string OrderApprovalPath = "/api/v1/purchasing/orders/{id}/approval";
    public const string OrderCancelImpactPath = "/api/v1/purchasing/orders/{id}/cancel-impact";
    public const string OrderTotalsPath = "/api/v1/purchasing/orders/{id}/totals";
    public const string OrderCancellationPath = "/api/v1/purchasing/orders/{id}/cancellation";
    public const string OrderLineSchedulePath = "/api/v1/purchasing/orders/{id}/lines/{lineId}/schedule";
    public const string ProductCandidatesPath = "/api/v1/purchasing/references/products";
    public const string CurrencyCandidatesPath = "/api/v1/purchasing/references/currencies";
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>The first commit and its replay both answer 201 (ADR-011 §4; the Sales #321 decision), told apart by the replay headers.</summary>
    public const int CreatedStatus = 201;

    /// <summary>A transition answers the record after it (200); the same key again answers 200 + replay headers.</summary>
    public const int MutationStatus = 200;
}

/// <summary>Shape validation only; the owners' scales, permissions and references are the service's.</summary>
public static class PurchasingJson
{
    private const int MaximumLines = 500;
    private const int MaximumProductVersionLength = 64;
    private static readonly Regex DecimalText = new("\\A[0-9]+(?:\\.[0-9]+)?\\z", RegexOptions.CultureInvariant);
    private static readonly Regex VersionText = new("\\A[1-9][0-9]*\\z", RegexOptions.CultureInvariant);

    public static JsonSerializerOptions Options { get; } = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static CreatePurchaseRequestRequest ReadCreateRequest(string json)
    {
        var request = JsonSerializer.Deserialize<CreatePurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (request.Lines is null || request.Lines.Count is < 1 or > MaximumLines)
        {
            throw new JsonException("Invalid request fields.");
        }

        var ids = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId) || line.ProductId == Guid.Empty || line.UnitId == Guid.Empty)
            {
                throw new JsonException("Invalid line fields.");
            }

            RequireProductVersionShape(line.ExpectedProductVersion);
            RequireDecimalShape(line.Quantity);
            if (line.Source is { } source
                && (!PurchaseRequestSourceKindsV1.TryParse(source.Kind, out _) || source.DocumentId == Guid.Empty || source.DocumentLineId == Guid.Empty))
            {
                throw new JsonException("Invalid line source.");
            }
        }

        return request;
    }

    public static SubmitPurchaseRequestRequest ReadSubmit(string json)
    {
        var request = JsonSerializer.Deserialize<SubmitPurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        return request;
    }

    public static ApprovePurchaseRequestRequest ReadApproveRequest(string json)
    {
        var request = JsonSerializer.Deserialize<ApprovePurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        return request;
    }

    /// <summary>The reason is a required string (shape); whether it is blank or too long is the domain's 422.</summary>
    public static RejectPurchaseRequestRequest ReadRejectRequest(string json)
    {
        var request = JsonSerializer.Deserialize<RejectPurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.Reason is null)
        {
            throw new JsonException("Invalid reason.");
        }

        return request;
    }

    /// <summary>A1-1b: the reason is a required string (shape); whether it is blank or too long is the domain's 422.</summary>
    public static WithdrawPurchaseRequestRequest ReadWithdrawRequest(string json)
    {
        var request = JsonSerializer.Deserialize<WithdrawPurchaseRequestRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.Reason is null)
        {
            throw new JsonException("Invalid reason.");
        }

        return request;
    }

    public static CreatePurchaseOrderRequest ReadCreateOrder(string json)
    {
        var request = JsonSerializer.Deserialize<CreatePurchaseOrderRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        if (request.SupplierPartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty || request.SourceRequestId == Guid.Empty
            || request.Lines is null || request.Lines.Count is < 1 or > MaximumLines)
        {
            throw new JsonException("Invalid order fields.");
        }

        var ids = new HashSet<Guid>();
        var sourceLines = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !ids.Add(line.LineId) || line.ProductId == Guid.Empty || line.UnitId == Guid.Empty
                || line.SourceRequestLineId == Guid.Empty || (line.SourceRequestLineId is { } source && !sourceLines.Add(source))
                || (line.SourceRequestLineId is not null && request.SourceRequestId is null))
            {
                throw new JsonException("Invalid line fields.");
            }

            RequireProductVersionShape(line.ExpectedProductVersion);
            RequireDecimalShape(line.Quantity);
            if (line.Price is { } price)
            {
                // A2a-1: the shape only — exact decimal text (an explicit 0 is a price), a non-empty price unit. Whether the
                // value is non-negative, within the money profile's decimals, in the line's unit or a valid discount is the domain's 422.
                if (price.PriceUnitId == Guid.Empty)
                {
                    throw new JsonException("Invalid price unit.");
                }

                RequireAmountShape(price.UnitPrice);
                if (price.DiscountPercent is not null)
                {
                    RequireAmountShape(price.DiscountPercent);
                }

                if (price.DiscountAmount is not null)
                {
                    RequireAmountShape(price.DiscountAmount);
                }

                // A2a-2: the maturity surcharge's shape only; its kind, bounds and the 0-price rule are the domain's 422.
                if (price.DeferralPercent is not null)
                {
                    RequireAmountShape(price.DeferralPercent);
                }

                if (price.DeferralAmount is not null)
                {
                    RequireAmountShape(price.DeferralAmount);
                }
            }
        }

        if (request.CurrencyId == Guid.Empty)
        {
            throw new JsonException("Invalid currency.");
        }

        return request;
    }

    public static ApprovePurchaseOrderRequest ReadApprove(string json)
    {
        var request = JsonSerializer.Deserialize<ApprovePurchaseOrderRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        return request;
    }

    /// <summary>A1-3: the reason is a required string (shape); whether it is blank or too long is the domain's 422.</summary>
    public static CancelPurchaseOrderRequest ReadCancelOrder(string json)
    {
        var request = JsonSerializer.Deserialize<CancelPurchaseOrderRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.Reason is null)
        {
            throw new JsonException("Invalid reason.");
        }

        return request;
    }

    /// <summary>
    /// A3 API-30: the shape only — 1..500 slices, non-empty distinct slice ids, exact decimal quantity text above zero (a zero
    /// quantity text such as <c>"0"</c> is refused here, a 400, like every other shape refusal). Whether each quantity fits the
    /// unit's scale and the slices sum to the line's quantity is the service's and the domain's 422.
    /// </summary>
    public static SetPurchaseOrderLineScheduleRequest ReadSetLineSchedule(string json)
    {
        var request = JsonSerializer.Deserialize<SetPurchaseOrderLineScheduleRequest>(json, Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersionShape(request.ExpectedVersion);
        if (request.Slices is null || request.Slices.Count < 1 || request.Slices.Count > Domain.Orders.PurchaseOrder.MaximumScheduleSlices)
        {
            throw new JsonException("Invalid slices.");
        }

        var ids = new HashSet<Guid>();
        foreach (var slice in request.Slices)
        {
            if (slice is null || slice.SliceId == Guid.Empty || !ids.Add(slice.SliceId) || slice.DeliveryDate == default)
            {
                throw new JsonException("Invalid slice fields.");
            }

            RequireDecimalShape(slice.Quantity);
        }

        return request;
    }

    private static void RequireVersionShape(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 19 || !VersionText.IsMatch(text))
        {
            throw new JsonException("Invalid expected version.");
        }
    }

    private static void RequireProductVersionShape(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumProductVersionLength
            || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
        {
            throw new JsonException("Invalid product version.");
        }
    }

    /// <summary>A2a-1: plain non-negative decimal text (digits with at most one inner '.'), at most 128 characters; zero is allowed here.</summary>
    private static void RequireAmountShape(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 128 || !DecimalText.IsMatch(text))
        {
            throw new JsonException("Invalid amount text.");
        }
    }

    private static void RequireDecimalShape(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 128 || !DecimalText.IsMatch(text) || !text.Any(c => c is >= '1' and <= '9'))
        {
            throw new JsonException("Invalid decimal text.");
        }
    }
}
