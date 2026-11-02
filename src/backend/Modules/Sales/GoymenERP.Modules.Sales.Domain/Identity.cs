namespace GoymenERP.Modules.Sales.Domain;

internal static class Identity
{
    public static Guid Require(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;
}

/// <summary>
/// The caller's expected version is not the current one: the record changed underneath it
/// (the Purchasing / Logistics / Manufacturing / Product pattern). Nothing is applied.
/// <see cref="Code"/> is <see cref="SalesRuleCodes.VersionConflict"/>.
/// </summary>
public sealed class SalesVersionConflictException(long expected, long current)
    : Exception("Satış taslağının sürümü değişti.")
{
    public string Code => SalesRuleCodes.VersionConflict;

    public long ExpectedVersion { get; } = expected;

    public long CurrentVersion { get; } = current;
}

/// <summary>
/// A sales rule refused the operation. <see cref="Code"/> is stable and machine-readable so a
/// test or a later application layer can tell WHICH rule held, never only that some rule did.
/// Nothing is partly applied when this is thrown. Caller errors (empty identity, null, an
/// undefined enum value, a non-positive quantity where a positive one is required) stay
/// <see cref="ArgumentException"/>s.
/// </summary>
public sealed class SalesRuleException(string code, string message, Guid? lineId = null) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    /// <summary>The client line id a per-line refusal is about (K3 Head B: the commercial revision names the line); null for a draft-level rule.</summary>
    public Guid? LineId { get; } = lineId;
}

/// <summary>The stable refusal codes of the Sales domain. All prefixed <c>sales_</c>.</summary>
public static class SalesRuleCodes
{
    /// <summary>Raised as <see cref="SalesVersionConflictException"/>, not as a rule refusal.</summary>
    public const string VersionConflict = "sales_version_conflict";

    public const string DraftNotDraft = "sales_draft_not_draft";
    public const string DraftWithoutLines = "sales_draft_without_lines";

    /// <summary>The internal approval transition is not allowed from the draft's current approval state (SALES-A1; also: lines of a submitted or approved draft do not change).</summary>
    public const string StateConflict = "sales_state_conflict";

    /// <summary>A rejection reason longer than the technical bound; refused, never cut.</summary>
    public const string ApprovalReasonTooLong = "sales_approval_reason_too_long";
    public const string TooManyLines = "sales_too_many_lines";
    public const string DuplicateLineId = "sales_duplicate_line_id";
    public const string NoteTooLong = "sales_note_too_long";

    /// <summary>A snapshot names another company than the draft's (server-resolved) company.</summary>
    public const string SnapshotCompanyMismatch = "sales_snapshot_company_mismatch";

    /// <summary>The product or money snapshot was taken for another date than the draft's business date.</summary>
    public const string SnapshotDateMismatch = "sales_snapshot_date_mismatch";

    /// <summary>The money snapshot was answered for another operation than the sales draft's (<c>sales.order-draft.v1</c>).</summary>
    public const string SnapshotOperationMismatch = "sales_snapshot_operation_mismatch";

    /// <summary>The selected order unit is not the unit the Product owner's snapshot binds as the order unit.</summary>
    public const string ProductUnitMismatch = "sales_product_unit_mismatch";

    /// <summary>The order unit profile is inactive or not effective on the draft's business date (the Settings port's validity obligation; CARİ #306 P2-2).</summary>
    public const string UnitProfileNotInEffect = "sales_unit_profile_not_in_effect";

    /// <summary>The money profile is inactive or not effective on the draft's business date (CARİ #306 P3-R1).</summary>
    public const string MoneyProfileNotInEffect = "sales_money_profile_not_in_effect";

    /// <summary>The order unit's dimension is neither kilogram nor a count dimension; Sales invents no unit for it (Ö2; only kilogram is approved in Settings today).</summary>
    public const string UnitUnsupported = "sales_unit_unsupported";

    /// <summary>The quantity's dimension (kilogram / piece) is not the order unit profile's dimension, or two quantities of different units met.</summary>
    public const string UnitMismatch = "sales_unit_mismatch";

    /// <summary>A quantity arithmetic would go below zero (a plan larger than what it plans).</summary>
    public const string QuantityBelowZero = "sales_quantity_below_zero";

    /// <summary>More decimals than the applied scale (min(profile, Ö2's 3)); refused, never rounded.</summary>
    public const string QuantityScaleExceeded = "sales_quantity_scale_exceeded";

    /// <summary>A unit price needs its price unit and a price unit needs a price (#257).</summary>
    public const string PriceUnitPairing = "sales_price_unit_pairing";

    // K3 (Sinan 5748258299): manual price and discount before a price list. Mathematical validity only —
    // no minimum price, margin floor or maximum discount is a rule here (none exists; none is invented).
    /// <summary>The unit price text carries more decimals than the approved money profile's scale; nothing is rounded.</summary>
    public const string PriceScaleExceeded = "sales_price_scale_exceeded";
    /// <summary>The price unit is none of the product owner's three unit bindings (stock, sales, order).</summary>
    public const string PriceUnitUnsupported = "sales_price_unit_unsupported";
    /// <summary>The discount percent is outside 0..100 (a percentage, not a threshold).</summary>
    public const string DiscountOutOfRange = "sales_discount_out_of_range";
    /// <summary>A discount without a unit price: a percentage of nothing (ASSUMPTION-SALESP-1).</summary>
    public const string DiscountWithoutPrice = "sales_discount_without_price";
    // AT-018 / AT-021 (user decision 2026-09-30): the list-price reference and the visible adjustments.
    /// <summary>A discount is a percent OR an amount, never both; a surcharge likewise.</summary>
    public const string AdjustmentKindConflict = "sales_adjustment_kind_conflict";
    /// <summary>An amount (discount or surcharge) carries more decimals than the money profile's scale; nothing is rounded.</summary>
    public const string AdjustmentScaleExceeded = "sales_adjustment_scale_exceeded";
    /// <summary>A surcharge percent outside 0..100.</summary>
    public const string SurchargeOutOfRange = "sales_surcharge_out_of_range";
    /// <summary>A surcharge, a discount amount or a reference without a unit price.</summary>
    public const string AdjustmentWithoutPrice = "sales_adjustment_without_price";
    /// <summary>A discount amount larger than the unit price: a net below zero.</summary>
    public const string DiscountExceedsPrice = "sales_discount_exceeds_price";
    /// <summary>The reference is in another price unit than the line's price.</summary>
    public const string PriceReferenceMismatch = "sales_price_reference_mismatch";
    /// <summary>The line quantity no longer belongs to the immutable referenced price tier.</summary>
    public const string PriceReferenceQuantityMismatch = "sales_price_reference_quantity_mismatch";
    /// <summary>The unit price cannot be represented exactly by the deterministic decimal calculator.</summary>
    public const string PriceValueUnrepresentable = "sales_price_value_unrepresentable";
    public const string DiscountPercentUnrepresentable = "sales_discount_percent_unrepresentable";
    public const string DiscountAmountUnrepresentable = "sales_discount_amount_unrepresentable";
    public const string SurchargePercentUnrepresentable = "sales_surcharge_percent_unrepresentable";
    public const string SurchargeAmountUnrepresentable = "sales_surcharge_amount_unrepresentable";
    /// <summary>All inputs are representable, but their exact intermediate arithmetic exceeds decimal.</summary>
    public const string PriceCalculationOverflow = "sales_price_calculation_overflow";
    /// <summary>
    /// AT-021: the net price is below the reference's floor. No approver for an exception is decided, so it is refused
    /// (fail-closed), never passed silently (ASSUMPTION-PRL-5).
    /// </summary>
    public const string PriceBelowFloor = "sales_price_below_floor";
    /// <summary>A below-floor commercial snapshot exists but its separate exception is not approved.</summary>
    public const string PriceFloorExceptionPending = "sales_price_floor_exception_pending";
    /// <summary>The exception command must name every current below-floor line exactly once.</summary>
    public const string PriceFloorExceptionLinesMismatch = "sales_price_floor_exception_lines_mismatch";
    /// <summary>The actor who requested the current exception cannot approve it.</summary>
    public const string PriceFloorExceptionSameActor = "sales_price_floor_exception_same_actor";
    /// <summary>An exception approval reason is required.</summary>
    public const string PriceFloorExceptionReasonRequired = "sales_price_floor_exception_reason_required";
    /// <summary>
    /// SAT-D2 / API-12: a line's positive discount exceeds the limit of the actor who set it (or no limit is defined for
    /// them, or the line was never checked) and is not approved by another person yet.
    /// </summary>
    public const string DiscountExceptionPending = "sales_discount_exception_pending";
    /// <summary>The discount exception command must name every current over-limit discount line exactly once.</summary>
    public const string DiscountExceptionLinesMismatch = "sales_discount_exception_lines_mismatch";
    /// <summary>The actor who set the over-limit discount cannot approve it (hazırlayan ≠ onaylayan).</summary>
    public const string DiscountExceptionSameActor = "sales_discount_exception_same_actor";
    /// <summary>A discount exception approval reason is required.</summary>
    public const string DiscountExceptionReasonRequired = "sales_discount_exception_reason_required";
    /// <summary>A commercial revision must name every current line exactly once and no other line.</summary>
    public const string RevisionLinesMismatch = "sales_revision_lines_mismatch";
    /// <summary>A commercial revision that changes no value is refused: an approval is never discarded for nothing (ASSUMPTION-SALESP-5).</summary>
    public const string RevisionNoChange = "sales_revision_no_change";
    /// <summary>
    /// DEC-SAL-002 quantity revision: the change carries its reason (the task's "gerekçe ile"); a blank reason is refused,
    /// never defaulted. The price revision keeps its own shape (no reason member), unchanged.
    /// </summary>
    public const string RevisionReasonRequired = "sales_revision_reason_required";
    /// <summary>A revision reason over the technical bound (the note's) is refused, never cut.</summary>
    public const string RevisionReasonTooLong = "sales_revision_reason_too_long";

    /// <summary>
    /// API-04 (fail-closed, NEEDS-SINAN): the customer or the currency of an order with a PRICED line is not changed — the
    /// prices were set (and, for a list reference, resolved by the SAT-D9 tier chain) for the old customer and currency, and
    /// no decision says whether they carry over or are restated.
    /// </summary>
    public const string HeaderRevisionPricedLines = "sales_header_revision_priced_lines";

    /// <summary>
    /// SAT-D152 (EK21): the customer of an order converted from a quote is never changed — for another customer the order is
    /// cancelled and a new quote or order is opened (the quote-to-order trail stays intact). The currency follows SAT-D128.
    /// </summary>
    public const string HeaderRevisionQuoteCustomer = "sales_header_revision_quote_customer";

    /// <summary>SAT-D123 (EK18): an order is not approved without due terms; they are entered (or changed) by a header revision.</summary>
    public const string DueTermsRequired = "sales_due_terms_required";

    /// <summary>DEC-SAL-003: a delivery-schedule update must name every current line exactly once and no other line.</summary>
    public const string ScheduleLinesMismatch = "sales_schedule_lines_mismatch";
    /// <summary>
    /// DEC-SAL-003 dates-only scope: a delivery-schedule update names each line's CURRENT slices exactly (same ids,
    /// none added or removed); the quantities are not re-split. Re-splitting is not in the decision's text and is not
    /// invented here.
    /// </summary>
    public const string ScheduleSlicesMismatch = "sales_schedule_slices_mismatch";
    /// <summary>A delivery-schedule update that changes no date is refused: no empty audited change is recorded.</summary>
    public const string ScheduleNoChange = "sales_schedule_no_change";

    /// <summary>
    /// DEC-SAL-005 / AT-007: a cancellation carries its reason (the master report's "silme yok … sebep ile"); a blank
    /// reason is refused, never defaulted.
    /// </summary>
    public const string CancellationReasonRequired = "sales_cancellation_reason_required";
    /// <summary>A cancellation reason over the technical bound (the note's) is refused, never cut.</summary>
    public const string CancellationReasonTooLong = "sales_cancellation_reason_too_long";

    /// <summary>No 30 ("Zorla kapatma … gerekçe zorunlu, audit"): a blank force-close reason is refused, never defaulted.</summary>
    public const string ForceCloseReasonRequired = "sales_force_close_reason_required";
    /// <summary>A force-close reason over the technical bound (the note's) is refused, never cut.</summary>
    public const string ForceCloseReasonTooLong = "sales_force_close_reason_too_long";

    // Partial delivery (user decision 2026-09-30): the undelivered remainder of a partly delivered line is cancelled or its
    // quantity revised, with approval; the delivered and loaded part is kept.
    /// <summary>The line has no recorded shipment yet: it is not partly delivered, so its remainder is not this command's (a revision is).</summary>
    public const string RemainderNotStarted = "sales_remainder_not_started";
    /// <summary>Nothing of the line is left beyond what the delivery owner has loaded: there is no remainder to cancel.</summary>
    public const string RemainderNone = "sales_remainder_none";
    /// <summary>The new quantity is below what the delivery owner has loaded (loaded ≥ shipped ≥ delivered): never reduced below it.</summary>
    public const string RemainderBelowFloor = "sales_remainder_below_floor";
    /// <summary>
    /// SAT-D104 (EK13): a quantity revision may not take a line below what the delivery owner (Logistics) has already SHIPPED
    /// for it; refused on that line.
    /// </summary>
    public const string RevisionBelowShipped = "sales_revision_below_shipped";
    /// <summary>
    /// SAT-D112 (EK14): a decrease may not take a line below its LOADED total (shipped + waiting on the vehicle, Logistics'
    /// cumulative loaded): a load already on the vehicle above the new quantity would otherwise only be stopped by Logistics'
    /// shipment-time cap (the safety net). Refused on that line, at the revision and again at the re-approval's submission and
    /// approval.
    /// </summary>
    public const string RevisionBelowLoaded = "sales_revision_below_loaded";
    /// <summary>The revised quantity equals the current one: an approval is not asked for nothing.</summary>
    public const string RemainderNoChange = "sales_remainder_no_change";
    /// <summary>The command names a line the order does not have.</summary>
    public const string RemainderLineUnknown = "sales_remainder_line_unknown";

    // API-20 / API-21 (plan §3.2, S-01): the sales request and its approval (delegation No 160, 161).
    /// <summary>A sales request is stored only with at least one line (1–500, the draft's bound).</summary>
    public const string RequestWithoutLines = "sales_request_without_lines";
    /// <summary>
    /// No 161 (SINAN_KARARLARI_2026-10-27 §1 for the same transition): a rejection carries its reason; a blank reason is
    /// refused, never defaulted.
    /// </summary>
    public const string RequestRejectionReasonRequired = "sales_request_rejection_reason_required";
    /// <summary>A rejection reason over the technical bound (the note's) is refused, never cut.</summary>
    public const string RequestRejectionReasonTooLong = "sales_request_rejection_reason_too_long";

    /// <summary>
    /// No 161 (fail-closed): a request with ANY order that is not cancelled — converted from a quote covering it, whatever its
    /// approval state, force-closed included — cannot be rejected; the order is cancelled first. The wire value keeps its
    /// first name (<c>…_has_approved_order</c>) so clients that decode it do not break; SAT-D23 confirms that a never-approved
    /// (pending) order blocks as well.
    /// </summary>
    public const string RequestHasApprovedOrder = "sales_request_has_approved_order";

    // API-22 / API-23 / API-24 (plan §3.2, S-01): the sales quote (delegation No 162, 163).
    /// <summary>A sales quote is stored only with at least one line (1–500, the draft's bound).</summary>
    public const string QuoteWithoutLines = "sales_quote_without_lines";
    /// <summary>A validity date before the quote's own business date is not a date range; refused (no default validity is chosen).</summary>
    public const string QuoteValidUntilBeforeDate = "sales_quote_valid_until_before_date";
    /// <summary>A quote names the same request twice, or more requests than the technical bound.</summary>
    public const string QuoteRequestsInvalid = "sales_quote_requests_invalid";
    /// <summary>
    /// A loss carries its reason: SAT-D25's entry of the Settings list, and the free-text explanation when that entry asks for
    /// one (the list's own flag); a blank explanation where one is required is refused, never defaulted.
    /// </summary>
    public const string QuoteLossReasonRequired = "sales_quote_loss_reason_required";
    /// <summary>A loss explanation over the technical bound (the note's) is refused, never cut.</summary>
    public const string QuoteLossReasonTooLong = "sales_quote_loss_reason_too_long";
    /// <summary>No 160: an order is not opened from a quote while any request it covers is not approved (default-deny).</summary>
    public const string QuoteRequestNotApproved = "sales_quote_request_not_approved";
    /// <summary>
    /// SAT-D24: the quote's validity date precedes the SERVER's business day (Europe/Istanbul, SAT-D10 — never a client date)
    /// and its current revision carries no recorded expired-quote approval (SAT-D44); the conversion is refused.
    /// </summary>
    public const string QuoteExpired = "sales_quote_expired";
    /// <summary>SAT-D24: an expired-quote approval is asked for a quote that is not expired on the server's business day.</summary>
    public const string QuoteNotExpired = "sales_quote_not_expired";
    /// <summary>SAT-D24: the quote's current revision already carries an expired-quote approval (one per revision).</summary>
    public const string QuoteExpiryAlreadyApproved = "sales_quote_expiry_already_approved";

    public const string TooManySlices = "sales_too_many_slices";
    public const string DuplicateSliceId = "sales_duplicate_slice_id";
    public const string SliceUnitMismatch = "sales_slice_unit_mismatch";

    /// <summary>The delivery slices of a line add up to more than the line; a shortfall is "unplanned", never "delivered".</summary>
    public const string SlicesExceedQuantity = "sales_slices_exceed_quantity";

    // ---- MOD-03 → Sales: the order's tax (SAT-D93/D97/D140/D147/D158) -----------------------------------------------------

    /// <summary>SAT-D140/D158: the buyer situation code is malformed (shape only; the Settings list decides membership).</summary>
    public const string TaxBuyerSituationInvalid = "sales_tax_buyer_situation_invalid";

    /// <summary>SAT-D158: the buyer situation is not in the Settings buyer situation list in force on the order's business date.</summary>
    public const string TaxBuyerSituationUnknown = "sales_tax_buyer_situation_unknown";

    /// <summary>SAT-D147: the exemption code is malformed (shape only).</summary>
    public const string TaxExemptionCodeInvalid = "sales_tax_exemption_code_invalid";

    /// <summary>SAT-D147 / SAT-D93: the exemption code is not in the Settings exemption list in force on the order's business date.</summary>
    public const string TaxExemptionCodeUnknown = "sales_tax_exemption_code_unknown";

    /// <summary>SAT-D147: an exemption code is chosen only for a situation whose effect is "KDV %0 with an exemption code".</summary>
    public const string TaxExemptionCodeNotApplicable = "sales_tax_exemption_code_not_applicable";

    /// <summary>SAT-D147: the buyer's document number and date are given together (and the number is at most 64 characters).</summary>
    public const string TaxBuyerDocumentIncomplete = "sales_tax_buyer_document_incomplete";

    /// <summary>SAT-D147: a buyer document is given only for a situation that requires one.</summary>
    public const string TaxBuyerDocumentNotApplicable = "sales_tax_buyer_document_not_applicable";

    /// <summary>SAT-D139: the item type of a line's tax override is not one of the Settings item types.</summary>
    public const string TaxItemTypeUnknown = "sales_tax_item_type_unknown";

    /// <summary>SAT-D97: a line's tax override needs a reason.</summary>
    public const string TaxOverrideReasonRequired = "sales_tax_override_reason_required";

    /// <summary>SAT-D97: the reason of a line's tax override is at most 4000 characters.</summary>
    public const string TaxOverrideReasonTooLong = "sales_tax_override_reason_too_long";

    /// <summary>SAT-D181: the picked VAT rate is not an entry of the Settings VAT rate list in force (or no list is entered).</summary>
    public const string TaxVatRateUnknown = "sales_tax_vat_rate_unknown";

    /// <summary>SAT-D181 (EK26) / SAT-D93 / SAT-D147: a picked VAT rate of 0 needs an exemption code from the exemption list.</summary>
    public const string TaxExemptionCodeRequired = "sales_tax_exemption_code_required";

    /// <summary>SAT-D181: the picked withholding ratio is not an entry of the Settings withholding ratio list in force (or none entered).</summary>
    public const string TaxWithholdingRatioUnknown = "sales_tax_withholding_ratio_unknown";

    /// <summary>SAT-D147/D181: a withholding ratio is picked only for a line that carries VAT (never KDV %0 or an exempt default).</summary>
    public const string TaxWithholdingNotApplicable = "sales_tax_withholding_not_applicable";

    /// <summary>
    /// SAT-D175/D176 (EK25): a subcontract service ("fason hizmet") order carries ONLY lines whose product's item type is service
    /// (SAT-D149); a goods line is sold on a separate, normal order. Also the answer when the item type cannot be confirmed.
    /// </summary>
    public const string SubcontractLineNotService = "sales_subcontract_line_not_service";

    /// <summary>
    /// SAT-D195 (EK29): a subcontract service line never uses the price list (no list reference, no floor exception); its price is
    /// typed per kilogram and approved by the order's internal approval.
    /// </summary>
    public const string SubcontractPriceListNotUsed = "sales_subcontract_price_list_not_used";

    /// <summary>
    /// SAT-D167 (EK24): a line with any shipment — a dispatch note, a loading instruction or a stock allocation (or a recorded
    /// remainder change) — is neither removed nor given another product.
    /// </summary>
    public const string LineRevisionLineShipped = "sales_line_revision_line_shipped";

    /// <summary>SAT-D167: a product change names a line added in the same revision, and each added line replaces at most one line.</summary>
    public const string LineReplacementInvalid = "sales_line_replacement_invalid";

    /// <summary>SAT-D167: a product change must name another product (the same product's quantity or price has its own revision).</summary>
    public const string LineReplacementSameProduct = "sales_line_replacement_same_product";

    /// <summary>SAT-D167: a line revision keeps at least one active line on the order (a wrong order is cancelled, DEC-SAL-005).</summary>
    public const string LineRevisionNoLinesLeft = "sales_line_revision_no_lines_left";

    /// <summary>SAT-D201 (EK31): a subcontract service order is not submitted while a service line has no per-kg price entered.</summary>
    public const string SubcontractPriceRequired = "sales_subcontract_price_required";

    /// <summary>SAT-D195 (EK29): a subcontract service is sold per kilogram — its order unit and its price unit are kilograms.</summary>
    public const string SubcontractUnitNotKilogram = "sales_subcontract_unit_not_kilogram";

    /// <summary>
    /// SAT-D175 (EK25): the customer of a subcontract service order does not change while a production plan or a customer-goods
    /// allocation is linked to it — or while that cannot be read (fail-closed).
    /// </summary>
    public const string SubcontractCustomerLocked = "sales_subcontract_customer_locked";

    /// <summary>A line's tax snapshot names another product than the line (a stale or crossed resolution; internal).</summary>
    public const string TaxSnapshotMismatch = "sales_tax_snapshot_mismatch";

    /// <summary>
    /// A line-level guard fired although the aggregate had already checked the same bound. The
    /// aggregate is the single authority; this code marks the second, defensive check.
    /// </summary>
    public const string InternalInvariant = "sales_internal_invariant";
}
