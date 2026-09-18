namespace GoymenERP.Modules.Manufacturing.Domain.Common;

/// <summary>
/// A manufacturing rule was broken. <see cref="Code"/> is one of <see cref="ManufacturingRuleCodes"/>:
/// stable and machine-readable, so a test or a later application layer can tell WHICH rule held
/// without parsing the message. Nothing is partly applied when this is thrown.
/// </summary>
public sealed class ManufacturingRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The stable refusal codes of the Manufacturing domain. Invalid arguments (an empty id, an
/// undefined enum value, a zero or negative mass where a positive one is required) stay
/// <see cref="ArgumentException"/>s: they are caller errors, not rules.
/// </summary>
public static class ManufacturingRuleCodes
{
    public const string KilogramPrecisionExceeded = "manufacturing_kilogram_precision_exceeded";
    public const string PlanWithoutInputs = "manufacturing_plan_without_inputs";
    public const string DuplicateRollInPlan = "manufacturing_duplicate_roll_in_plan";
    public const string PlanNotDraft = "manufacturing_plan_not_draft";
    public const string PlanNotApproved = "manufacturing_plan_not_approved";
    public const string OrderClosed = "manufacturing_order_closed";
    public const string RollNotInOrder = "manufacturing_roll_not_in_order";
    public const string RollNotIssued = "manufacturing_roll_not_issued";
    public const string InputAlreadyIssued = "manufacturing_input_already_issued";
    public const string ReturnAlreadyRecorded = "manufacturing_return_already_recorded";
    public const string ReturnExceedsIssued = "manufacturing_return_exceeds_issued";
    public const string OutputAlreadyDeclared = "manufacturing_output_already_declared";
    public const string WipAlreadyMeasured = "manufacturing_wip_already_measured";
    public const string WipMeasurementNotLatest = "manufacturing_wip_measurement_not_latest";

    public const string UnexplainedDifference = "manufacturing_unexplained_difference";

    /// <summary>
    /// A stored plan or order does not rebuild under the rules that wrote it (MFG-5): a close trace
    /// with only one of its two parts, a version that is not the one its facts add up to, or a
    /// stored cost the domain cannot have produced. The record is refused, never repaired.
    /// </summary>
    public const string StoredRecordInvalid = "manufacturing_stored_record_invalid";

    /// <summary>Raised as <see cref="ManufacturingVersionConflictException"/>, not as a rule refusal.</summary>
    public const string VersionConflict = "manufacturing_version_conflict";
}

/// <summary>
/// The caller's expected version is not the record's current one: it changed underneath the
/// caller (MFG-3, SİPARİŞ #292 P3-a; the Purchasing and Logistics pattern). Nothing is applied.
/// <see cref="Code"/> is <see cref="ManufacturingRuleCodes.VersionConflict"/>.
/// </summary>
public sealed class ManufacturingVersionConflictException(long expected, long current)
    : Exception("Üretim kaydının sürümü değişti.")
{
    public string Code => ManufacturingRuleCodes.VersionConflict;

    public long ExpectedVersion { get; } = expected;

    public long CurrentVersion { get; } = current;
}
