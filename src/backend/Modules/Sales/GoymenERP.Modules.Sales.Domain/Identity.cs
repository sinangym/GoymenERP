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

    public const string TooManySlices = "sales_too_many_slices";
    public const string DuplicateSliceId = "sales_duplicate_slice_id";
    public const string SliceUnitMismatch = "sales_slice_unit_mismatch";

    /// <summary>The delivery slices of a line add up to more than the line; a shortfall is "unplanned", never "delivered".</summary>
    public const string SlicesExceedQuantity = "sales_slices_exceed_quantity";

    /// <summary>
    /// A line-level guard fired although the aggregate had already checked the same bound. The
    /// aggregate is the single authority; this code marks the second, defensive check.
    /// </summary>
    public const string InternalInvariant = "sales_internal_invariant";
}
