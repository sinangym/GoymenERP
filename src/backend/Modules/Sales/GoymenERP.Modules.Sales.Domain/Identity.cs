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
public sealed class SalesRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
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
